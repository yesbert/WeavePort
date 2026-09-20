# Release scheduler activity before completing the public task

## Why
A caller awaiting a scheduled result could observe stale active ownership and receive an unnecessary immediate-restart rejection.

## What Changes
Release active state and signal idle before completing the call task under the scheduler lock. Assert idle state after sequential completed invocations.

## Impact
ScheduledPluginHost completion ordering and public reuse regressions; no public signature or worker isolation policy change.
