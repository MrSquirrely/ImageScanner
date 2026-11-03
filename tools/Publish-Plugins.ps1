<#
.SYNOPSIS
    Publishes the plugins in tools\marketplace-plugins.json to the PixShelf Marketplace.

.DESCRIPTION
    1. Builds each plugin in Release, which writes artifacts\plugins\<Project>.zip (see build\Plugin.targets).
    2. Uploads the zips to a GitHub release (created if needed; existing assets are replaced).
    3. Creates or updates one discussion per plugin in its Discussions category, in the format the
       discussion forms produce, with the release download link and the zip's SHA-256.

    Posts made by the repository owner get PixShelf's "Team" badge, and plugins installed from them count as
    official. Running it again after changing a plugin publishes the new build: the zips, hashes and posts are
    updated in place. Bump "version" in marketplace-plugins.json (and the plugin's Version) so the app offers it
    as an update.

    Needs the GitHub CLI signed in to an account that can write to the repository:
        winget install GitHub.cli
        gh auth login
    The Discussions categories must exist first (GitHub has no API to create them); see the README.

.PARAMETER Tag
    Release tag that hosts the zips. Defaults to "plugins-<yyyy.MM.dd>".

.PARAMETER DryRun
    Build the zips and print the posts without contacting GitHub.

.EXAMPLE
    .\tools\Publish-Plugins.ps1 -DryRun
.EXAMPLE
    .\tools\Publish-Plugins.ps1 -Only pixshelf.converter
#>
param(
    [string]$Repository = "MrSquirrely/ImageScanner",
    [string]$Tag = "plugins-$(Get-Date -Format 'yyyy.MM.dd')",
    [string[]]$Only = @(),
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $PSScriptRoot "marketplace-plugins.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$plugins = @($manifest.plugins | Where-Object { $Only.Count -eq 0 -or $Only -contains $_.id })
if ($plugins.Count -eq 0) { throw "No plugins match -Only $($Only -join ', ')." }

function Invoke-Gh {
    $output = & gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh $($args -join ' ') failed." }
    return $output
}

if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "The GitHub CLI isn't installed. Install it with 'winget install GitHub.cli', then run 'gh auth login'."
    }
    & gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The GitHub CLI isn't signed in. Run 'gh auth login' first." }
}

# ImageSharp 4 refuses Release builds without its license. sixlabors.lic is git-ignored, so a git worktree
# doesn't have it: also look in the main checkout. A key or file set in the environment wins.
$licenseArgs = @()
if (-not $env:SixLaborsLicenseKey -and -not $env:SixLaborsLicenseFile) {
    $candidates = @(Join-Path $root "sixlabors.lic")
    $commonDir = & git -C $root rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -eq 0 -and $commonDir) {
        $candidates += Join-Path (Split-Path $commonDir -Parent) "sixlabors.lic"
    }

    $license = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $license) {
        throw "No Six Labors license found (ImageSharp needs it for Release builds). Put sixlabors.lic in the repository root, or set the SixLaborsLicenseFile or SixLaborsLicenseKey environment variable."
    }

    $licenseArgs = @("-p:SixLaborsLicenseFile=$((Resolve-Path $license).Path)")
}

# 1. Build and hash
$packages = foreach ($plugin in $plugins) {
    Write-Host "Building $($plugin.name)..."
    & dotnet build (Join-Path $root "$($plugin.project)\$($plugin.project).csproj") -c Release -v q -nologo @licenseArgs | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Building $($plugin.project) failed." }
    $zip = Join-Path $root "artifacts\plugins\$($plugin.project).zip"
    if (-not (Test-Path $zip)) { throw "The build didn't produce $zip." }
    [pscustomobject]@{
        Plugin = $plugin
        Zip    = $zip
        Sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash
        Url    = "https://github.com/$Repository/releases/download/$Tag/$([uri]::EscapeDataString((Split-Path $zip -Leaf)))"
    }
}

# The discussion body, exactly as the category's form renders it (the app finds each value by its heading)
function Get-Body($package) {
    $plugin = $package.Plugin
    $source = "https://github.com/$Repository/tree/master/$($plugin.project)"
    return @"
### Plugin name

$($plugin.name)

### Plugin ID

$($plugin.id)

### Version

$($plugin.version)

### Description

$($plugin.description)

### Download URL

$($package.Url)

### SHA-256

$($package.Sha256)

### Source code

$source

### Safety

- [X] The download contains only my plugin and its dependencies, and doesn't collect data without saying so above.
"@
}

if ($DryRun) {
    foreach ($package in $packages) {
        Write-Host ""
        Write-Host "=== [$($package.Plugin.category)] $($package.Plugin.title)" -ForegroundColor Cyan
        Write-Host "    $($package.Zip) ($([math]::Round((Get-Item $package.Zip).Length / 1KB)) KB)"
        Write-Host (Get-Body $package)
    }
    Write-Host ""
    Write-Host "Dry run: nothing was uploaded or posted." -ForegroundColor Yellow
    return
}

# 2. Release with the zips
$owner, $name = $Repository.Split('/')
& gh release view $Tag --repo $Repository 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating release $Tag..."
    Invoke-Gh release create $Tag --repo $Repository --title "PixShelf plugins ($Tag)" `
        --notes "Plugin downloads for the PixShelf Marketplace (Settings -> Marketplace). Each zip's SHA-256 is in its discussion post." `
        --latest=false | Out-Null
}
Write-Host "Uploading $($packages.Count) zip(s) to $Tag..."
Invoke-Gh release upload $Tag --repo $Repository --clobber @($packages | ForEach-Object { $_.Zip }) | Out-Null

# 3. Discussions
$query = @'
query($owner: String!, $name: String!) {
  repository(owner: $owner, name: $name) {
    id
    discussionCategories(first: 50) { nodes { id slug name } }
    discussions(first: 100, orderBy: { field: CREATED_AT, direction: DESC }) { nodes { id title url category { slug } } }
  }
}
'@
$repo = (Invoke-Gh api graphql -f query=$query -F owner=$owner -F name=$name | ConvertFrom-Json).data.repository

foreach ($package in $packages) {
    $plugin = $package.Plugin
    $category = $repo.discussionCategories.nodes | Where-Object { $_.slug -eq $plugin.category }
    if (-not $category) {
        throw "The Discussions category '$($plugin.category)' doesn't exist yet. Create it on GitHub (Discussions -> Categories) and run this again."
    }

    $body = Get-Body $package
    $existing = $repo.discussions.nodes | Where-Object { $_.category.slug -eq $plugin.category -and $_.title -eq $plugin.title } | Select-Object -First 1
    if ($existing) {
        $mutation = 'mutation($id: ID!, $body: String!) { updateDiscussion(input: { discussionId: $id, body: $body }) { discussion { url } } }'
        $result = Invoke-Gh api graphql -f query=$mutation -F id=$($existing.id) -f body=$body | ConvertFrom-Json
        Write-Host "Updated  $($result.data.updateDiscussion.discussion.url)  ($($plugin.name) $($plugin.version))"
    }
    else {
        $mutation = 'mutation($repo: ID!, $category: ID!, $title: String!, $body: String!) { createDiscussion(input: { repositoryId: $repo, categoryId: $category, title: $title, body: $body }) { discussion { url } } }'
        $result = Invoke-Gh api graphql -f query=$mutation -F repo=$($repo.id) -F category=$($category.id) -f title=$($plugin.title) -f body=$body | ConvertFrom-Json
        Write-Host "Created  $($result.data.createDiscussion.discussion.url)  ($($plugin.name) $($plugin.version))"
    }
}

Write-Host ""
Write-Host "Done. The listings appear in PixShelf under Settings -> Marketplace (it caches the list for 10 minutes; use Refresh)." -ForegroundColor Green
