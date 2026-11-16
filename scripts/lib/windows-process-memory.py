"""Sample one process and its descendants; stdin EOF/newline ends sampling."""
import ctypes as c
from ctypes import wintypes as w
import json
import sys
import threading
import time

root = int(sys.argv[1])
interval = float(sys.argv[2])
kernel = c.WinDLL("kernel32", use_last_error=True)
psapi = c.WinDLL("psapi", use_last_error=True)


class Entry(c.Structure):
    _fields_ = [("size", w.DWORD), ("usage", w.DWORD), ("pid", w.DWORD),
                ("heap", c.c_size_t), ("module", w.DWORD), ("threads", w.DWORD),
                ("parent", w.DWORD), ("priority", w.LONG), ("flags", w.DWORD),
                ("exe", w.WCHAR * 260)]


class Memory(c.Structure):
    _fields_ = [("size", w.DWORD), ("faults", w.DWORD)] + [
        (name, c.c_size_t) for name in ["peakRss", "rss", "peakPaged", "paged",
                                     "peakNonpaged", "nonpaged", "pagefile", "peakPagefile", "private"]]


kernel.CreateToolhelp32Snapshot.argtypes = [w.DWORD, w.DWORD]
kernel.CreateToolhelp32Snapshot.restype = w.HANDLE
kernel.Process32FirstW.argtypes = [w.HANDLE, c.POINTER(Entry)]
kernel.Process32NextW.argtypes = [w.HANDLE, c.POINTER(Entry)]
kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
kernel.OpenProcess.restype = w.HANDLE
kernel.CloseHandle.argtypes = [w.HANDLE]
psapi.GetProcessMemoryInfo.argtypes = [w.HANDLE, c.POINTER(Memory), w.DWORD]
stop = threading.Event()
threading.Thread(target=lambda: (sys.stdin.readline(), stop.set()), daemon=True).start()
while not stop.is_set():
    snapshot = kernel.CreateToolhelp32Snapshot(2, 0)
    if snapshot == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    entry = Entry(size=c.sizeof(Entry))
    parents = {}
    try:
        valid = kernel.Process32FirstW(snapshot, c.byref(entry))
        while valid:
            parents[entry.pid] = entry.parent
            valid = kernel.Process32NextW(snapshot, c.byref(entry))
    finally:
        kernel.CloseHandle(snapshot)
    family = {root}
    while True:
        children = {pid for pid, parent in parents.items() if parent in family} - family
        if not children:
            break
        family.update(children)
    values = []
    for pid in family:
        handle = kernel.OpenProcess(0x410, False, pid)
        if not handle:
            continue
        try:
            memory = Memory(size=c.sizeof(Memory))
            if psapi.GetProcessMemoryInfo(handle, c.byref(memory), memory.size):
                values.append({"pid": pid, "rss": memory.rss, "private": memory.private})
        finally:
            kernel.CloseHandle(handle)
    print(json.dumps({"at": time.time() * 1000, "processes": values}), flush=True)
    stop.wait(interval)
