param(
    [string]$Message = "Shop oi gia bao nhieu?",
    [string]$FromId = "123456789",
    [string]$FromName = "Test User",
    [string]$PostId = "1100652266458019_101"
)

$configPath = Join-Path $PSScriptRoot "..\WebhookService\appsettings.Development.json"
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$appSecret = $config.Facebook.AppSecret
$pageId = $config.Facebook.PageId

$commentId = "{0}_{1}_{2}" -f $pageId, "101", [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

$payloadObject = @{
    object = "page"
    entry  = @(
        @{
            id      = $pageId
            time    = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            changes = @(
                @{
                    field = "feed"
                    value = @{
                        from       = @{
                            id   = $FromId
                            name = $FromName
                        }
                        item       = "comment"
                        post_id    = $PostId
                        comment_id = $commentId
                        message    = $Message
                        verb       = "add"
                    }
                }
            )
        }
    )
}

$body = $payloadObject | ConvertTo-Json -Depth 10 -Compress
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)

$hmacsha = New-Object System.Security.Cryptography.HMACSHA256
$hmacsha.key = [Text.Encoding]::UTF8.GetBytes($appSecret)
$signature = $hmacsha.ComputeHash($bodyBytes)
$signatureHex = ([BitConverter]::ToString($signature) -replace '-', '').ToLower()
$headerValue = "sha256=$signatureHex"

Write-Host "Sending webhook event..." -ForegroundColor Cyan
Write-Host "CommentId: $commentId"
Write-Host "Message: $Message"
Write-Host "Signature: $headerValue"

try {
    $response = Invoke-RestMethod `
        -Uri "http://localhost:3001/webhook" `
        -Method Post `
        -Body $bodyBytes `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{ "X-Hub-Signature-256" = $headerValue }

    Write-Host "`nResponse from WebhookService:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 6 | Write-Host
}
catch {
    Write-Host "`nFailed to send webhook: $_" -ForegroundColor Red
}
