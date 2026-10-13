import { InlineNotice } from "../../ui/PageState.tsx";

const documentationRoot = "https://github.com/sck03/rustdoc/blob/main/docs/";

export function MaintenanceOperationsGuide({ pending }: { pending: boolean }) {
  return (
    <details className="maintenance-advanced-details" open={pending} aria-label="服务器维护步骤">
      <summary>{pending ? "下一步：请部署管理员完成恢复" : "服务器恢复与升级步骤"}</summary>
      {pending ? <InlineNotice tone="warning">恢复文件已校验并安排，当前数据尚未替换。请暂停录入，由部署管理员完成下面的维护，再重新登录核对。</InlineNotice> : null}
      <h3>恢复：网页安排后还需一次服务器维护</h3>
      <ol>
        <li>{pending ? "确认安排恢复前已另存当前系统的完整迁移包；暂停录入，等待重要文件任务完成。" : "先保存当前系统的完整迁移包，通知大家暂停录入，等待重要文件任务完成。"}</li>
        <li>在网页选择备份或迁移包，核对文件和密码，点击安排恢复，等待“恢复已暂存”。</li>
        <li>部署管理员使用原部署目录、数据目录、镜像和连接配置执行恢复维护。成功后重新登录，核对账号、单据、附件、模板与 PDF。</li>
      </ol>
      <p>Linux Docker：进入原源码目录和 PowerShell 7，按部署记录恢复原来的 $run 参数（RuntimeRoot、Image、BindAddress、Port）后执行：</p>
      <p className="settings-helper-text"><code>./scripts/run-native-docker.ps1 @run -RestorePending</code></p>
      <p>脚本会停应用、恢复数据库与必要文件、检查结果并重新启动。维护失败时保留停止状态，请检查错误并重试同一命令，不要删除数据卷或待恢复标记。</p>
      <p>非容器网页服务：先停止 API，由管理员用相同 AppRoot/DataRoot 和独立维护连接运行 ExportDocManager.Server 的 --restore-pending，成功后再启动。普通刷新或重启不能代替恢复。</p>
      <h3>升级：先备份，再更新镜像，最后核对</h3>
      <ol>
        <li>在网页“完整迁移与换机”中创建并下载完整迁移包，另外保管部署配置及包密码。</li>
        <li>部署管理员取得匹配的新版本部署文件，将原 $run.Image 改为新发布镜像，在原目录执行下面的正常部署命令。</li>
        <li>等待自动数据库升级和服务就绪，再核对版本、登录、附件及 PDF。升级程序时不使用恢复参数；数据库已升级后不能随意换回旧程序。</li>
      </ol>
      <p className="settings-helper-text"><code>./scripts/run-native-docker.ps1 @run -SkipBuild</code></p>
      <p>以上更新命令用于已发布镜像；从源码构建本地镜像时省略 -SkipBuild。$run 是原部署参数，不是密码；新开终端需先按部署记录恢复它。</p>
      <div className="toolbar-actions">
        <a className="command-button secondary" href={documentationRoot + encodeURIComponent("Docker容器部署与启动指南.md")} target="_blank" rel="noreferrer">查看 Linux Docker 部署说明</a>
        <a className="command-button secondary" href={documentationRoot + encodeURIComponent("服务器备份迁移与恢复指南.md")} target="_blank" rel="noreferrer">查看备份、迁移与恢复教程</a>
      </div>
    </details>
  );
}
