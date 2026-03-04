Write-Host "=== Shelf Life Identity Validation ===" -ForegroundColor Cyan
$errors = 0
$csFiles = Get-ChildItem -Path "Assets" -Recurse -Filter *.cs
if (!$csFiles) {
    Write-Host "⚠ No C# files found in Assets. Ensure you are in 'My project' folder." -ForegroundColor Yellow
    exit
}
# בדיקת Rigidbody
$rigid = $csFiles | Select-String -Pattern "Rigidbody " -SimpleMatch
if ($rigid) {
    Write-Host "❌ DNA Violation: Rigidbody detected!" -ForegroundColor Red
    $errors++
}
# בדיקת מצלמה
$cameraCheck = $csFiles | Select-String -Pattern "transform\.rotation"
if ($cameraCheck) {
    Write-Host "⚠ Manual rotation detected — verify 20° Y lock" -ForegroundColor Yellow
}
if ($errors -eq 0) {
    Write-Host "✅ Identity Check Passed. Studio is secure." -ForegroundColor Green
} else {
    Write-Host "❌ Identity Violations Found." -ForegroundColor Red
}
