# How to Create a Release

A step-by-step guide for shipping a new version of CenterHub.

---

## 1. Decide the version number

CenterHub uses **semantic versioning**: `MAJOR.MINOR.PATCH`

| Change type | Which number to bump | Example |
|---|---|---|
| Breaking change / major redesign | MAJOR | 5.x.x → 6.0.0 |
| New feature or page | MINOR | 5.4.x → 5.5.0 |
| Bug fix or small improvement | PATCH | 5.4.0 → 5.4.1 |

The current version is always in `CenterHubNew.csproj`:

```xml
<Version>5.4.1</Version>
```

---

## 2. Make your changes and build clean

Make sure everything compiles with zero errors before proceeding:

```powershell
dotnet build -c Release --nologo
```

Fix any errors. Warnings are fine.

---

## 3. Bump the version

Edit `CenterHubNew.csproj` and update the `<Version>` tag:

```xml
<Version>5.5.0</Version>   <!-- whatever the new version is -->
```

---

## 4. Commit your changes

Stage only the files you actually changed (avoid `git add .` — it can pull in build artifacts):

```powershell
git add CenterHubNew.csproj
git add MVVM/Services/SomeService.cs   # whichever files you changed
git add MVVM/ViewModel/SomeViewModel.cs
```

Write a clear commit message in this format:

```powershell
git commit -m "Release v5.5.0 — short description of what changed"
```

---

## 5. Create and push a tag

The tag name must match the version exactly, with a `v` prefix:

```powershell
git tag v5.5.0
git push origin master
git push origin v5.5.0
```

---

## 6. Create the GitHub release

Use the `gh` CLI (already installed). Swap in your version and release notes:

```powershell
gh release create v5.5.0 `
    --title "v5.5.0 — Short title here" `
    --notes "## What's new

### Feature or fix category
- **Thing you added** — brief description
- **Another thing** — brief description"
```

This publishes the release on GitHub at:
`https://github.com/MikWil/CenterHubPC/releases/tag/v5.5.0`

---

## 7. (Optional) Build the MSI installer

If you want to ship an installable `.msi` alongside the release, run:

```powershell
.\build-installer.ps1
```

The signed MSI ends up in `publish/Release/`. You can attach it to the GitHub release:

```powershell
gh release upload v5.5.0 .\publish\Release\CenterHubSetup.msi
```

---

## Quick-reference checklist

- [ ] Code builds clean (`dotnet build -c Release`)
- [ ] Version bumped in `CenterHubNew.csproj`
- [ ] Changes committed with a clear message
- [ ] Tag created and pushed (`git tag vX.Y.Z && git push origin vX.Y.Z`)
- [ ] GitHub release created (`gh release create ...`)
- [ ] MSI built and uploaded (optional)
