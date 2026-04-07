# APS Source Control Protocol

## ✅ Current State (Fixed)

```
main (production) ──[v1.0.0-planning-material-improvements tag]
  ↑ (only merges from release branches)
  │
develop (integration)
  ↓ (base for all feature/fix branches)
  ├─ feature/* (work on new features)
  ├─ bugfix/* (work on non-critical bugs)
  ├─ hotfix/* (critical production fixes)
  └─ release/* (prepare releases)
```

## Proper Workflow for Each Task Type

### 🚀 New Feature (e.g., Material Panel)
```bash
# 1. Create feature branch FROM develop
git checkout develop
git pull origin develop
git checkout -b feature/material-panel-improvements

# 2. Make commits with conventional format
git commit -m "feat(planning): improve material panel with auto-load"

# 3. Push to remote
git push origin feature/material-panel-improvements -u

# 4. Create PR on GitHub (base: develop)

# 5. After approval and merge:
git checkout develop
git pull origin develop
git branch -d feature/material-panel-improvements
```

### 🐛 Non-Critical Bug Fix
```bash
# 1. Create bugfix branch FROM develop
git checkout develop
git pull origin develop
git checkout -b bugfix/po-merge-calculation

# 2. Fix and commit
git commit -m "fix(planning): correct PO grouping logic"

# 3. Push and create PR to develop
git push origin bugfix/po-merge-calculation -u

# 4. After merge, cleanup
git checkout develop
git pull origin develop
git branch -d bugfix/po-merge-calculation
```

### 🔥 Critical Production Hotfix
```bash
# 1. Create hotfix branch FROM main
git checkout main
git pull origin main
git checkout -b hotfix/critical-api-error

# 2. Fix and commit
git commit -m "fix(api): handle null planning orders"

# 3. Create PR to main
git push origin hotfix/critical-api-error -u

# 4. After merge and tagging on main:
git checkout main
git pull origin main
git tag -a v1.0.1 -m "Hotfix v1.0.1: description"
git push origin v1.0.1

# 5. Merge back to develop
git checkout develop
git pull origin develop
git merge main
git push origin develop
```

### 📦 Release to Production
```bash
# 1. Create release branch FROM develop
git checkout develop
git pull origin develop
git checkout -b release/v1.1.0

# 2. Update version numbers, docs
# (only version bumps and docs, NO feature additions)
git commit -m "chore(release): bump version to v1.1.0"

# 3. Create PR to main
git push origin release/v1.1.0 -u

# 4. After approval and merge to main:
git checkout main
git pull origin main
git tag -a v1.1.0 -m "Release v1.1.0: description of features"
git push origin v1.1.0

# 5. Merge back to develop
git checkout develop
git pull origin develop
git merge main
git push origin develop

# 6. Delete release branch
git push origin --delete release/v1.1.0
git branch -d release/v1.1.0
```

## Branch Protection Rules

### `main` (Production)
- ✅ Requires pull request reviews (minimum 1)
- ✅ Requires all checks to pass
- ✅ No direct pushes allowed
- ✅ Only receives merges from `release/*` and `hotfix/*` branches
- ✅ Every merge is tagged with semantic version

### `develop` (Integration)
- ✅ Requires pull request reviews (minimum 1)
- ✅ Requires all checks to pass
- ✅ Only receives merges from `feature/*`, `bugfix/*`, and `release/*` branches
- ✅ Can accept force pushes (for history cleanup)

### `feature/*` / `bugfix/*` / `hotfix/*` / `release/*` (Temporary)
- ✅ No protection rules
- ✅ Deleted after merge
- ✅ Based on either `develop` (features) or `main` (hotfixes)

## Commit Message Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `style`: Code formatting
- `refactor`: Code restructure (no feature/bug change)
- `perf`: Performance improvement
- `test`: Test additions/updates
- `chore`: Build, deps, release tasks

### Examples
```
feat(planning): add material panel auto-load
fix(api): correct heat size calculation
docs(git): update branching strategy
chore(release): bump version to v1.1.0
```

## Tagging Strategy

### Production Tags (on `main`)
- Format: `v<MAJOR>.<MINOR>.<PATCH>`
- Examples:
  - `v1.0.0` - Initial release
  - `v1.0.1` - Hotfix
  - `v1.1.0` - Minor feature release
  - `v2.0.0` - Major release

### Pre-release Tags (optional)
- `v1.0.0-alpha.1`
- `v1.0.0-beta.1`
- `v1.0.0-rc.1`

## Key Rules to Remember

1. **main = production only**
   - Only release-ready code on main
   - Every commit on main is tagged with version

2. **develop = integration hub**
   - All features merged here for testing
   - Base branch for all feature work

3. **Feature branches are temporary**
   - Delete after merge to develop
   - Do NOT merge other branches into feature branches
   - Do NOT commit directly to develop or main

4. **One feature per branch**
   - Avoid mixing features
   - Easier to rollback if needed

5. **Pull requests are mandatory**
   - Reviews catch bugs
   - Maintain code quality
   - Document decisions

6. **Keep history clean**
   - Use conventional commits
   - Squash trivial commits if needed
   - Rebase on base branch before merging

## Mistake Recovery

### Oops! I committed to main/develop directly
```bash
# Undo last commit, keep changes
git reset --soft HEAD~1

# Create feature branch with those changes
git checkout -b feature/my-feature
git commit -m "feat(...): description"
```

### Oops! I merged the wrong branch
```bash
# Revert the merge commit (safe, creates new commit)
git revert -m 1 <merge-commit-hash>
git push origin main
```

### Oops! History is messed up
```bash
# If not yet pushed: reset and retry
git reset --hard <correct-commit>

# If already pushed: use revert instead
git revert <wrong-commit>
```

## Current Branches Status

| Branch | Purpose | Status |
|--------|---------|--------|
| `main` | Production | ✅ Clean, protected, v1.0.0-planning-material-improvements |
| `develop` | Integration | ✅ In sync with main, ready for features |
| `fix/aps-correctness-blockers` | Old fix work | ⚠️ Can delete (merged into main) |

## Going Forward

When working on ANY fix or feature:
1. Create branch FROM develop (or main for hotfixes)
2. Work in isolation
3. Merge back via pull request
4. Delete branch after merge
5. Keep main clean and tagged

**Never mix branches!** Each task gets its own branch. ✅
