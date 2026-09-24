"""Sequential owned cleanup with primary and secondary causes preserved."""

from .context import SessionCleanupError


async def cleanup_all(*actions):
    errors = []
    for action in actions:
        try:
            await action()
        except BaseException as error:
            errors.append(error)
    if errors:
        raise SessionCleanupError(
            "Owned resource cleanup failed", errors=errors
        ) from errors[0]


async def execute_with_cleanup(action, cleanup):
    primary = None
    try:
        return await action()
    except BaseException as error:
        primary = error
        raise
    finally:
        try:
            await cleanup()
        except BaseException as secondary:
            if primary is None:
                raise
            failure = SessionCleanupError(
                "Execution and cleanup failed", errors=[primary, secondary]
            )
            failure.has_execution_failure = True
            raise failure from primary
