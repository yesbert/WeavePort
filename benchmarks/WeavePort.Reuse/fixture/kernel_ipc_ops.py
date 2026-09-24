"""Docker-only adversarial fixture: kernel IPC can outlive a Python process."""

import ctypes
import os
import errno
import platform
from pathlib import Path

if not Path("/.dockerenv").exists() or Path(__file__).resolve() != Path(
    "/fixture/kernel_ipc_ops.py"
):
    raise RuntimeError("Disposable Docker fixture only")


def library():
    libc = ctypes.CDLL(None, use_errno=True)
    libc.shmget.argtypes = [ctypes.c_int, ctypes.c_size_t, ctypes.c_int]
    libc.shmat.argtypes = [ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
    libc.shmat.restype = ctypes.c_void_p
    libc.shmdt.argtypes = [ctypes.c_void_p]
    return libc


def kernel_leave(request, scope, libc):
    shmid = libc.shmget(0, 4096, 0o1000 | 0o600)
    if shmid < 0:
        raise OSError(ctypes.get_errno(), "shmget")
    address = libc.shmat(shmid, None, 0)
    if address == ctypes.c_void_p(-1).value:
        raise OSError(ctypes.get_errno(), "shmat")
    data = scope.tenant.encode() + b"\0"
    ctypes.memmove(address, data, len(data))
    libc.shmdt(address)
    return {"shmid": shmid}


def kernel_probe(request, scope, libc):
    address = libc.shmat(request["shmid"], None, 0o10000)
    if address == ctypes.c_void_p(-1).value:
        return {"accessible": False, "canary": None}
    value = ctypes.string_at(address, 64).split(b"\0", 1)[0].decode(errors="replace")
    libc.shmdt(address)
    return {"accessible": True, "canary": value}


def kernel_semaphore(request, scope, libc):
    result = libc.semget(0, 1, 0o1000 | 0o600)
    if result < 0:
        raise OSError(ctypes.get_errno(), "semget")
    return {"semid": result}


def kernel_messages(request, scope, libc):
    result = libc.msgget(0, 0o1000 | 0o600)
    if result < 0:
        raise OSError(ctypes.get_errno(), "msgget")
    return {"msqid": result}


def posix_queue(request, scope, libc):
    result = libc.mq_open(b"/wp-canary", os.O_CREAT | os.O_RDWR, 0o600, None)
    if result < 0:
        raise OSError(ctypes.get_errno(), "mq_open")
    libc.mq_close(result)
    return {"created": True}


def execute(request, scope):
    libc = library()
    if request["op"] == "kernel_keyring":
        # Probe only this disposable process's private keyring; never touch the user/session rings.
        number = {"aarch64": 217, "x86_64": 248}[platform.machine()]
        result = libc.syscall(number, b"user", b"weaveport-canary", b"fixture", 7, -2)
        return {"blocked": result < 0 and ctypes.get_errno() == errno.EPERM}
    if request["op"] == "kernel_leave":
        return kernel_leave(request, scope, libc)
    if request["op"] == "kernel_probe":
        return kernel_probe(request, scope, libc)
    if request["op"] == "kernel_semaphore":
        return kernel_semaphore(request, scope, libc)
    if request["op"] == "kernel_messages":
        return kernel_messages(request, scope, libc)
    if request["op"] == "posix_queue":
        return posix_queue(request, scope, libc)
    raise ValueError("Unknown kernel IPC operation")
