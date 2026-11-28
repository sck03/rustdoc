import assert from 'node:assert/strict';
import net from 'node:net';

// Separate the online-file route from the backup route, then sever the former.
// The real storage services and backup buckets remain reachable for cold recovery.
export async function isolateOnlineS3Routes(targets) {
  const gates = [];
  const close = async () => {
    for (const { server, sockets } of gates) {
      for (const socket of sockets) socket.destroy();
      if (server.listening) await new Promise(resolve => server.close(resolve));
    }
  };
  try {
    const routed = [];
    for (const target of targets) {
      const url = new URL(target.endpoint);
      assert.equal(url.protocol, 'http:');
      assert.equal(url.hostname, '127.0.0.1', 'Fault injection requires runner-owned loopback services');
      const sockets = new Set();
      const track = socket => { sockets.add(socket); socket.on('close', () => sockets.delete(socket)); return socket; };
      const server = net.createServer(client => {
        track(client);
        const upstream = track(net.connect(Number(url.port || 80), url.hostname));
        client.on('error', () => upstream.destroy()); upstream.on('error', () => client.destroy());
        client.on('close', () => upstream.destroy()); upstream.on('close', () => client.destroy());
        client.pipe(upstream); upstream.pipe(client);
      });
      gates.push({ server, sockets });
      await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
      routed.push({ ...target, endpoint: 'http://127.0.0.1:' + server.address().port, backupEndpoint: target.endpoint });
    }
    return { targets: routed, close };
  } catch (error) { await close(); throw error; }
}
