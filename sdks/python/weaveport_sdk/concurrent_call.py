"""State retained for one admitted invocation until its terminal acknowledgement."""

import asyncio
from dataclasses import dataclass, field

from .context import PluginContext


@dataclass
class ConcurrentCall:
    request: dict
    active: bool = True
    cancelled: bool = False
    callbacks: list[asyncio.Future] = field(default_factory=list)
    context: PluginContext | None = None
    task: asyncio.Task | None = None
