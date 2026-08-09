# Interactive Sessions

An interactive session connects a workspace to a live provider conversation and its terminal runtime. It is where interactive AI work happens, but it is not the durable workspace itself.

## Three Separate Things

- **Provider conversation:** the OpenCode conversation identity and context.
- **Terminal runtime:** the LocalHost-owned process and terminal stream for that conversation.
- **Presentation:** a Windows Terminal or local browser view attached to the terminal runtime.

Multiple presentation types can refer to the same underlying session. They are not independent copies of the conversation.

## Start Or Resume

First use `Open Workspace` and wait for workspace readiness. Then create a new interactive session or select an existing one and attach it. LocalHost starts or reuses the provider runtime before granting the presentation attachment.

Creating a session does not publish work. Attaching does not create a Save Point.

## Detach

Detach releases the active presentation while preserving the interactive session when it remains recoverable. Use it when you want to leave the terminal without intentionally ending the provider conversation.

Closing a terminal window can also end the presentation. It does not delete the workspace, but an abrupt close may require reconnect or recovery handling.

## Reconnect

Reconnect attaches a new presentation to a detached or recoverable session. Output retained by the terminal service can be replayed to the new presentation. Recovery availability is bounded; use the status and recovery deadline shown by the app.

## Take Over

Only one presentation controls a session at a time. If another presentation owns it, request takeover to transfer control intentionally. Takeover can detach the previous presentation, so do not use it merely because a window is slow to appear.

## Restart

Restart the provider conversation/runtime when the interactive process has exited, failed, or must be recreated. Restarting a session is not the same as:

- stopping or rebuilding the workspace runtime
- restoring workspace files
- creating a new Working Copy
- creating a Save Point

If the whole workspace runtime is stopped or unhealthy, fix workspace readiness first and then reconnect or restart the session.

## Presentation Choices

- **Windows Terminal:** current desktop attach implementation on Windows.
- **Local browser:** loopback presentation backed by the same LocalHost session service.
- **Remote browser:** separate opt-in RemoteBridge deployment; disabled by default and not part of normal local attach.

Desktop terminal attachment is not yet supported by the current Linux or macOS packages.

## What Is Durable?

Repository files and recorded recovery data are the durable assets. A provider conversation may be resumable, but it should not be the only place important decisions, code, or deliverables exist. Write useful outcomes into the workspace and create Save Points.
