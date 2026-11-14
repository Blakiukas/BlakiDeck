$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$violations = @()

# Scan product XAML, not downloaded diagnostic decoder/runtime directories.
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Ekrano_valdymas') -Recurse -Filter '*.xaml' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw
        if ($content -match '(?s)<TextBlock\.RenderTransform>.*?<TranslateTransform[^>]*\bY\s*=') {
            $violations += "$($_.FullName): TextBlock uses TranslateTransform Y. Use OpticallyCenteredText."
        }
        if ($content -match '(?s)<TextBlock\b[^>]*\bMargin\s*=\s*"[^"]*-\d') {
            $violations += "$($_.FullName): TextBlock uses a negative margin. Use layout or OpticallyCenteredText."
        }
    }

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}
