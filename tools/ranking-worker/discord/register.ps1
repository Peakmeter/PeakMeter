# Registers PeakMeter's slash commands with Discord. Asks for the bot token without showing it; the token is only
# kept in this process and sent to discord.com.   Run:  & .\discord\register.ps1
$ErrorActionPreference = 'Stop'
$secure = Read-Host 'Bot-Token einfuegen (wird nicht angezeigt)' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $env:DISCORD_BOT_TOKEN = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $env:DISCORD_APP_ID = '1565554825637273734'
    node (Join-Path $PSScriptRoot 'register.mjs')
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Item Env:DISCORD_BOT_TOKEN -ErrorAction SilentlyContinue
}
