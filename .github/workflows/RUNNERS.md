# Running CI in the cloud or on a local machine

`ci.yml` runs either on GitHub's hosted runners (**cloud**) or on self-hosted runners on the
maintainer's machine (**local**). One repository variable chooses:

```bash
gh variable set CI_RUNNER --body local --repo albahadly/rulewright   # self-hosted runners
gh variable set CI_RUNNER --body cloud --repo albahadly/rulewright   # GitHub-hosted
gh variable delete CI_RUNNER --repo albahadly/rulewright             # unset = cloud
```

**Branches.** Work happens on `dev`; `main` is the pull-request target. `ci.yml` fires on a push
to `main`, on a pull request and by hand, so **a push to `dev` runs no CI**: open a pull request
from `dev`, or run it on the branch with `gh workflow run ci.yml --ref dev --repo albahadly/rulewright`.
The Pages site (`blazor-builder-pages.yml`) deploys only from `main`, so it ships on merge.
`main` accepts changes only through a pull request: the ruleset *main: pull requests only* (no
bypass, not even for admins) also blocks force-pushes and deleting it, and the clone's
`.git/hooks/pre-push` refuses a push to `main` before anything is sent.

A single run can override it: **Actions → CI → Run workflow → runner** (`default` follows the
variable). `publish-nuget.yml` follows the same switch (Linux runner only) and has the same
`runner` input; it is started only by a maintainer, never by a fork. `blazor-builder-pages.yml`
always runs in the cloud.

| Job | `cloud` | `local` |
|---|---|---|
| `windows` (net8.0, net10.0, net48, the net48 sample) | `windows-latest` | `[self-hosted, windows]` |
| `linux` (net8.0, net10.0, console sample, pack) | `ubuntu-latest` | `[self-hosted, linux]` |

## This repository is public: forks never run locally

A self-hosted runner executes whatever the workflow tells it to, as the account running it, on
that machine. Two guards keep a stranger's pull request off it:

1. **`ci.yml` sends a fork's pull request to the cloud** whatever `CI_RUNNER` says.
2. **Every outside contributor's run waits for approval** (*Settings → Actions → General → Fork
   pull request workflows → Require approval for all external contributors*; set through the API
   as `approval_policy: all_external_contributors`). This is the guard that matters: a fork's pull
   request runs the workflow file **from the fork**, so it can rewrite guard 1 away. Before you
   approve a fork's run, read any change it makes under `.github/`.

Check the setting has not been relaxed:

```bash
gh api repos/albahadly/rulewright/actions/permissions/fork-pr-contributor-approval
```

## The runners

A runner belongs to one repository, so these are separate from DocWright's, on the same machine:

| Runner | Folder | Start |
|---|---|---|
| `THINKPADP16` (Windows) | `%USERPROFILE%\actions-runner-rulewright` | `run.cmd` |
| `THINKPADP16-wsl` (WSL Ubuntu-24.04) | `~/actions-runner-rulewright` | `./run.sh` |

`C:\Users\albah\Start-CiRunners.cmd` starts all four runners (DocWright's and these). They run
as the user, not as services, so CI runs only while they are up.

On a self-hosted runner the workflow skips `actions/setup-dotnet` (it installs into Program Files,
which a runner running as a normal user cannot write) and uses what the machine has installed:
a .NET 10 SDK and the .NET 8 runtime (plus ASP.NET Core 8 on Linux), and on Windows the .NET
Framework 4.8 targeting pack.
