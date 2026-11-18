# Dev probe: ask Antigravity's local language server for quota info and print only the quota-related fields.
$p = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'language_server.exe' } | Select-Object -First 1
if (-not $p) { 'Antigravity language server not running'; exit }
$token = [regex]::Match($p.CommandLine, '--csrf_token[= ]+(\S+)').Groups[1].Value
$port = [regex]::Match($p.CommandLine, '--https_server_port[= ]+(\d+)').Groups[1].Value
$ports = @($port) + (Get-NetTCPConnection -OwningProcess $p.ProcessId -State Listen | ForEach-Object LocalPort) | Where-Object { $_ } | Select-Object -Unique
Add-Type @'
using System.Net; using System.Security.Cryptography.X509Certificates;
public class TrustLocal : ICertificatePolicy { public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p) { return true; } }
'@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustLocal
[System.Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$body = '{"metadata":{"ideName":"antigravity","extensionName":"antigravity","locale":"en"}}'
foreach ($pt in $ports) {
  foreach ($scheme in 'https','http') {
    try {
      $r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "${scheme}://127.0.0.1:$pt/exa.language_server_pb.LanguageServerService/GetUserStatus" `
        -Headers @{ 'X-Codeium-Csrf-Token' = $token; 'Connect-Protocol-Version' = '1' } -ContentType 'application/json' -Body $body -TimeoutSec 5
      "OK $scheme port $pt"
      $j = $r.Content | ConvertFrom-Json
      $j.userStatus.PSObject.Properties.Name -join ', '
      $cfg = $j.userStatus.cascadeModelConfigData.clientModelConfigs
      $cfg | ForEach-Object { '{0} | remaining={1} | reset={2}' -f $_.label, $_.quotaInfo.remainingFraction, $_.quotaInfo.resetTime }
      exit
    } catch { "fail $scheme $pt : $($_.Exception.Message.Substring(0,[Math]::Min(80,$_.Exception.Message.Length)))" }
  }
}
