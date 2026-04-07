# APS Git Workflow & Branching Strategy

## Overview
This document outlines the Git branching strategy, workflow, and best practices for the APS (Advanced Planning System) project.

---

## Branch Structure

### Main Branches (Protected)

#### `main` (Production)
- **Purpose**: Stable, production-ready code
- **Naming**: N/A (implicit)
- **Protection Rules**:
  - Requires pull request reviews (minimum 1)
  - Requires all checks to pass before merge
  - No direct pushes allowed
- **Update Strategy**: Merge from `release/` or hotfix branches only
- **Tagging**: Each merge to main gets a version tag (v1.0.0, v1.1.0, etc.)

#### `develop` (Integration)
- **Purpose**: Development integration branch; base for feature development
- **Naming**: N/A (implicit)
- **Protection Rules**:
  - Requires pull request reviews (minimum 1)
  - Requires all checks to pass before merge
- **Update Strategy**: Merge from feature, bugfix, and release branches
- **Release Cycle**: Prepare releases from develop

---

### Supporting Branches (Temporary)

#### Feature Branches: `feature/*`
- **Purpose**: New features or enhancements
- **Naming Convention**: `feature/descriptive-name`
  - Examples: `feature/material-panel`, `feature/heat-visualization`
- **Base Branch**: `develop`
- **Merge Strategy**: Pull request → `develop`
- **Lifecycle**: Delete after merge
- **Commit Messages**: Descriptive, imperative mood

#### Bugfix Branches: `bugfix/*`
- **Purpose**: Non-critical bug fixes
- **Naming Convention**: `bugfix/issue-description`
  - Examples: `bugfix/po-merge-calculation`, `bugfix/bom-filtering`
- **Base Branch**: `develop`
- **Merge Strategy**: Pull request → `develop`
- **Lifecycle**: Delete after merge

#### Release Branches: `release/*`
- **Purpose**: Prepare new production release
- **Naming Convention**: `release/v1.0.0` (matches version tag)
- **Base Branch**: `develop`
- **Workflow**:
  1. Create from `develop` when ready for release
  2. Only version bumps, bug fixes, documentation
  3. Merge to `main` with version tag
  4. Merge back to `develop`
- **Merge Strategy**: Pull request → `main` AND `develop`
- **Lifecycle**: Delete after merge

#### Hotfix Branches: `hotfix/*`
- **Purpose**: Critical production fixes
- **Naming Convention**: `hotfix/issue-description`
  - Examples: `hotfix/critical-planning-bug`, `hotfix/api-endpoint-error`
- **Base Branch**: `main`
- **Workflow**:
  1. Create from `main` when critical issue discovered
  2. Fix the issue and test thoroughly
  3. Merge to `main` with hotfix version tag (v1.0.1)
  4. Merge back to `develop`
- **Merge Strategy**: Pull request → `main` AND `develop`
- **Lifecycle**: Delete after merge

---

## Version Tagging

### Tag Format
```
v<MAJOR>.<MINOR>.<PATCH>
v1.0.0
v1.0.1 (hotfix)
v1.1.0 (minor release)
v2.0.0 (major release)
```

### Pre-release Tags (Optional)
```
v1.0.0-alpha.1
v1.0.0-beta.1
v1.0.0-rc.1
```

### Tag Scope
- **main branch**: Full release tags (v1.0.0, v1.1.0, v2.0.0)
- **develop branch**: No tags (development snapshots)
- **Feature/bugfix branches**: No tags (in-progress work)

---

## Commit Message Convention

### Format
```
<type>(<scope>): <subject>

<body>

<footer>
```

### Type (Required)
- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation
- **style**: Code style changes (formatting, missing semicolons, etc.)
- **refactor**: Code refactoring without feature/bug changes
- **perf**: Performance improvements
- **test**: Test additions/updates
- **chore**: Build, dependencies, release tasks

### Scope (Optional)
- Component or module affected: planning, bom, api, ui, etc.
- Example: `feat(planning): add material constraint check`

### Subject (Required)
- Imperative mood: "add" not "added" or "adds"
- No period at end
- Max 50 characters
- Lowercase first letter

### Body (Optional)
- Detailed explanation of why change is needed
- Wrap at 72 characters
- Separate from subject with blank line

### Footer (Optional)
- Reference issues: `Fixes #123`, `Closes #456`
- Breaking changes: `BREAKING CHANGE: description`

### Examples
```
feat(planning): integrate BOM explosion for material checks

Add material side panel showing inventory status for SO/PO
production requirements. Enables planners to review material
constraints before finalizing production orders.

Fixes #42
```

```
fix(api): correct PO-to-heat conversion ratio

The heat size calculation was using raw qty instead of
normalized MT values, causing incorrect batch sizing.

Closes #38
```

---

## Workflow Example: New Feature

### 1. Start Feature
```bash
# Update develop
git checkout develop
git pull origin develop

# Create feature branch
git checkout -b feature/new-planning-control

# Make changes, commit regularly
git add .
git commit -m "feat(planning): add new planning control"
```

### 2. Push & Create PR
```bash
git push origin feature/new-planning-control -u

# Create PR on GitHub:
# - Base: develop
# - Compare: feature/new-planning-control
# - Title: matches commit message type
# - Description: explains what, why, how
```

### 3. Review & Merge
```bash
# After review and approvals:
# Merge PR (squash or rebase if multiple commits)
# Delete branch after merge

# Sync local
git checkout develop
git pull origin develop
```

---

## Workflow Example: Release

### 1. Create Release Branch
```bash
git checkout develop
git pull origin develop
git checkout -b release/v1.0.0

# Update version numbers in config, docs, etc.
git commit -m "chore(release): bump version to v1.0.0"
git push origin release/v1.0.0 -u
```

### 2. Create PR to Main
```bash
# PR: release/v1.0.0 → main
# Title: Release v1.0.0
# After approval and merge:
git checkout main
git pull origin main

# Create tag
git tag -a v1.0.0 -m "Release v1.0.0: description..."
git push origin v1.0.0
```

### 3. Merge Back to Develop
```bash
git checkout develop
git pull origin develop
git merge release/v1.0.0
git push origin develop

# Delete release branch
git push origin --delete release/v1.0.0
```

---

## Workflow Example: Hotfix

### 1. Create Hotfix Branch
```bash
git checkout main
git pull origin main
git checkout -b hotfix/critical-bug

# Fix the issue
git add .
git commit -m "fix(api): critical endpoint error"
git push origin hotfix/critical-bug -u
```

### 2. Create PR to Main
```bash
# PR: hotfix/critical-bug → main
# After approval and merge:
git checkout main
git pull origin main

# Create hotfix tag
git tag -a v1.0.1 -m "Hotfix v1.0.1: description..."
git push origin v1.0.1
```

### 3. Merge Back to Develop
```bash
git checkout develop
git pull origin develop
git merge hotfix/critical-bug
git push origin develop

# Delete hotfix branch
git push origin --delete hotfix/critical-bug
```

---

## CI/CD Integration

### GitHub Actions Checklist
- [ ] Run tests on PR
- [ ] Lint code style
- [ ] Check security vulnerabilities
- [ ] Build project
- [ ] Auto-deploy to staging from develop
- [ ] Auto-deploy to production from main

---

## Code Review Guidelines

### PR Requirements
1. ✓ Descriptive title and detailed description
2. ✓ All tests passing
3. ✓ Linting/style checks passing
4. ✓ At least 1 approval (2 for main branch merges)
5. ✓ No merge conflicts
6. ✓ Branch up-to-date with base

### Review Checklist
- [ ] Code follows style guide
- [ ] Commits are logical/atomic
- [ ] Tests are adequate
- [ ] Documentation updated
- [ ] Performance impact considered
- [ ] Security implications reviewed

---

## Useful Git Commands

```bash
# List all branches
git branch -a

# List all tags
git tag -l

# Delete local branch
git branch -d feature/branch-name

# Delete remote branch
git push origin --delete feature/branch-name

# Rebase on develop (keep history clean)
git rebase develop

# View commit history with graph
git log --graph --oneline --decorate --all

# Undo last commit (keep changes)
git reset --soft HEAD~1

# Undo last commit (discard changes)
git reset --hard HEAD~1

# Stash changes temporarily
git stash
git stash list
git stash pop

# Cherry-pick a commit
git cherry-pick <commit-hash>
```

---

## Branch Protection Settings (GitHub)

### For `main`:
- ✓ Require pull request reviews (1 dismissal)
- ✓ Dismiss stale PR approvals when new commits pushed
- ✓ Require status checks to pass before merge
- ✓ Require branches to be up to date before merge
- ✓ Require code reviews before merge
- ✓ Do not allow bypassing rules
- ✓ Restrict who can push to main branch

### For `develop`:
- ✓ Require pull request reviews (1 dismissal)
- ✓ Require status checks to pass before merge
- ✗ Allow force pushes (optional)

---

## File Handling

### Always Commit
- Source code (.py, .js, .ts, .html, .css)
- Configuration files
- Documentation (.md)
- Tests

### Never Commit
- `__pycache__/`, `node_modules/`, `.venv/`
- `.env` (use .env.example instead)
- Excel temp files (`~$*.xlsx`)
- IDE settings (`.vscode/`)
- Build artifacts

### Check .gitignore
Maintained in repository root. Current patterns:
```
__pycache__/
*.py[cod]
.venv/
.env
~$*.xlsx
node_modules/
```

---

## Release Checklist

Before merging to main:
- [ ] All tests passing
- [ ] Documentation updated
- [ ] Version bumped
- [ ] CHANGELOG updated
- [ ] Code reviewed and approved
- [ ] Performance tested
- [ ] Security reviewed
- [ ] Tag prepared with release notes

---

## Questions?

For questions about Git workflow, branching, or commit practices:
1. Check this document first
2. Refer to [Git documentation](https://git-scm.com/doc)
3. Ask team lead or open a discussion

---

**Last Updated**: 2026-04-07
**Maintained By**: Development Team
