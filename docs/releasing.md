# Releasing (maintainer notes)

How to cut a versioned release of the KrZ-W/Sonarr fork. See
[../FORK.md](../FORK.md#versioning) for the versioning scheme.

## Version format recap

```
git tag / GitHub release :  v<upstream-version>+krzw.<N>     e.g. v4.0.17.2950+krzw.1
docker image tag         :  <upstream-version>-krzw.<N>      e.g. 4.0.19.2979-krzw.1
```

- `<upstream-version>` = the Sonarr version `personal/all-features-main` is rebased onto.
  Confirm it with:

  ```bash
  git describe --tags --abbrev=0 --match 'v*' \
    "$(git merge-base upstream/main personal/all-features-main)"
  ```

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

3. **Commit** the changelog (and any doc updates):

   ```bash
   git commit -am "docs: release v4.0.17.2950+krzw.1"
   git push myfork personal/all-features-main
   ```

4. **Tag and push the tag.** The `+` is fine in a git tag:

   ```bash
   git tag -a 'v4.0.17.2950+krzw.1' -m 'Fork release based on Sonarr 4.0.17.2950'
   git push myfork 'v4.0.17.2950+krzw.1'
   ```

   This triggers `docker-release.yml`, which builds and pushes the immutable image tag
   `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.1` (it maps `+` → `-` automatically).

5. **Create the GitHub release** from the tag, using the changelog section as the body:

   ```bash
   gh release create 'v4.0.17.2950+krzw.1' \
     --repo KrZ-W/Sonarr \
     --title 'v4.0.17.2950+krzw.1' \
     --notes-file <(sed -n '/## \[v4.0.17.2950+krzw.1\]/,/## \[/p' CHANGELOG.md | sed '$d')
   ```

## After rebasing onto a newer upstream

1. Rebase each `feature/*` / `fix/*` branch onto the new `upstream/main`, re-merge into
   `personal/all-features-main`, resolve conflicts.
2. Re-confirm the new `<upstream-version>` with the `git describe` command above.
3. Add an `[Unreleased]` → new-version section noting the rebase, then release as
   `v<new-upstream-version>+krzw.1`.

## CI overview

| Workflow | Trigger | Produces |
|---|---|---|
| `docker-image.yml` | push to `personal/**`, `feature/**`, `fix/**` | `:latest` (primary branch), `:<branch>`, `:sha-<short>` |
| `docker-release.yml` | push of a `v*` tag | `:<upstream-version>-krzw.<N>` (immutable release image) |
