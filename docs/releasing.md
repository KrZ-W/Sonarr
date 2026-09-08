# Releasing (maintainer notes)

How to cut a versioned release of the KrZ-W/Sonarr fork. See
[../FORK.md](../FORK.md#versioning) for the versioning scheme.

## Version format recap

```
git tag / GitHub release :  v<upstream-version>+krzw.<N>     e.g. v4.0.19.2979+krzw.2
docker image tag         :  <upstream-version>-krzw.<N>      e.g. 4.0.19.2979-krzw.2
```

- `<upstream-version>` = the Sonarr version `personal/all-features-main` is rebased onto.
  Confirm it with:

  ```bash
  git describe --tags --abbrev=0 --match 'v*' --exclude '*krzw*' personal/all-features-main
  ```

  (This clone has a single remote, `origin` = the fork. Upstream release tags are
  reachable from the aggregate because each rebase starts from one; `--exclude`
  skips the fork's own tags. `origin/main` and `origin/develop` are upstream
  mirrors refreshed by hand, not the base.)

- `<N>` starts at `1` for each new upstream base and increments for subsequent fork
  releases on that **same** base. After a rebase onto a newer upstream, reset to `1`.

## Steps

1. **Make sure `personal/all-features-main` is in the state you want to ship** and the
   image builds (the `docker-image.yml` workflow builds branch pushes).

2. **Update `CHANGELOG.md`:**
   - Move the entries under `[Unreleased]` into a new
     `## [v<ver>+krzw.<N>] — based on Sonarr <upstream-version>` section.
   - Reset `[Unreleased]` to `_Nothing yet._`.
   - Update the two link-reference lines at the bottom of the file.
   - Update **`FORK.md`**'s `Current fork version` line to the new tag (it is easy to
     miss and silently goes stale across releases).

3. **Commit** the changelog (and any doc updates):

   ```bash
   git commit -am "docs: release v4.0.19.2979+krzw.2"
   git push origin personal/all-features-main
   ```

4. **Tag and push the tag.** The `+` is fine in a git tag:

   ```bash
   git tag -a 'v4.0.19.2979+krzw.2' -m 'Fork release based on Sonarr 4.0.19.2979'
   git push origin 'v4.0.19.2979+krzw.2'
   ```

   This triggers `docker-release.yml`, which builds and pushes the immutable image tag
   `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2` (it maps `+` → `-` automatically).

5. **Boot-test the release image before announcing it.** A green CI build is not
   proof the image runs — this fork's `v4.0.19.2979+krzw.1` image built green but
   crash-looped in production (fixed in `krzw.2` by pinning the build SDK):

   ```bash
   docker pull ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2
   docker run -d --name sonarr-boot-test -p 18989:8989 ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2
   sleep 20
   curl -sf http://localhost:18989/ping    # expect {"status":"OK"}
   docker rm -f sonarr-boot-test
   ```

   If the container restarts or `/ping` fails, fix and cut the next `krzw.<N+1>` —
   never announce an image that hasn't booted.

6. **Create the GitHub release** from the tag, using the changelog section as the body:

   ```bash
   gh release create 'v4.0.19.2979+krzw.2' \
     --repo KrZ-W/Sonarr \
     --title 'v4.0.19.2979+krzw.2' \
     --notes-file <(sed -n '/## \[v4.0.19.2979+krzw.2\]/,/## \[/p' CHANGELOG.md | sed '$d')
   ```

   (Or paste the changelog section into the web UI.)

## After rebasing onto a newer upstream

1. Fetch the new upstream release tag without adding a remote
   (`git fetch https://github.com/Sonarr/Sonarr.git tag v4.0.20.xxxx`), rebase
   `personal/all-features-main` onto it (`git rebase --rebase-merges --onto <tag> <old-tag>`
   on a `feature/rebase-<ver>` working branch, resolving merge-replay conflicts from the
   original merge commits), verify, then replace the aggregate. Topic branches are
   intertwined and are left on their original base; cherry-picks still apply.
2. Re-confirm the new `<upstream-version>` with the `git describe` command above.
3. Refresh the `## Source` commit hashes in `docs/features/*.md` — a rebase rewrites
   every fork commit, so the cited hashes go stale. Find the new ones with
   `git log --oneline <upstream-base>..HEAD`. Update the example versions in this file
   and in `FORK.md` at the same time.
4. Re-verify the Dockerfile SDK pin still matches what upstream's `global.json` needs
   (see `5bb406f4f` — the krzw.1 crash-loop), then boot-test (step 5 above) before
   releasing.
5. Add an `[Unreleased]` → new-version section noting the rebase, then release as
   `v<new-upstream-version>+krzw.1`.

## CI overview

| Workflow | Trigger | Produces |
|---|---|---|
| `docker-image.yml` | push to `personal/**`, `feature/**`, `feat/**`, `fix/**` | `:latest` (primary branch), `:<branch>`, `:sha-<short>` |
| `docker-release.yml` | push of a `v*krzw*` tag (fork release tags) | `:<upstream-version>-krzw.<N>` (immutable release image) |
