[CmdletBinding()]
param(
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot "runtime"),
    [string]$EnvironmentFile = (Join-Path $PSScriptRoot ".env"),
    [string]$PostgreSqlDatabase = "exportdoc",
    [string]$PostgreSqlUsername = "exportdoc",
    [Parameter(Mandatory = $true)]
    [string]$PostgreSqlPassword,
    [Parameter(Mandatory = $true)]
    [string]$BootstrapToken,
    [int]$WebPort = 8080,
    [string]$WebBindAddress = "127.0.0.1",
    [int]$HttpsPort = 8443,
    [string]$ContainerSubnet,
    [string]$ReverseProxyIp,
    [switch]$RegenerateNetwork,
    [switch]$AllowNetworkOverlap,
    [switch]$AllowHttpDisasterRecovery
)

$ErrorActionPreference = "Stop"

$parsedWebBindAddress = $null
if (-not [System.Net.IPAddress]::TryParse($WebBindAddress, [ref]$parsedWebBindAddress) -or
    $parsedWebBindAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $parsedWebBindAddress.ToString() -cne $WebBindAddress) {
    throw "WebBindAddress 必须是 IPv4 地址，例如 127.0.0.1 或 0.0.0.0。"
}

function ConvertTo-IPv4Number {
    param([Parameter(Mandatory = $true)][System.Net.IPAddress]$Address)

    if ($Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "只支持 IPv4 地址：$Address"
    }

    $bytes = $Address.GetAddressBytes()
    return ([uint64]$bytes[0] * 16777216) +
        ([uint64]$bytes[1] * 65536) +
        ([uint64]$bytes[2] * 256) +
        [uint64]$bytes[3]
}

function ConvertFrom-IPv4Number {
    param([Parameter(Mandatory = $true)][uint64]$Value)

    if ($Value -gt [uint64]4294967295) {
        throw "IPv4 数值超出范围：$Value"
    }

    $first = [uint64][Math]::Floor($Value / 16777216) % 256
    $second = [uint64][Math]::Floor($Value / 65536) % 256
    $third = [uint64][Math]::Floor($Value / 256) % 256
    $fourth = $Value % 256
    return [System.Net.IPAddress]::Parse("$first.$second.$third.$fourth")
}

function Get-IPv4CidrRange {
    param([Parameter(Mandatory = $true)][string]$Cidr)

    $parts = $Cidr.Trim().Split('/')
    if ($parts.Count -ne 2) {
        throw "容器网段必须使用 IPv4 CIDR，例如 172.30.238.0/24：$Cidr"
    }

    $address = $null
    $prefixLength = 0
    if (-not [System.Net.IPAddress]::TryParse($parts[0], [ref]$address) -or
        $address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
        -not [int]::TryParse($parts[1], [ref]$prefixLength) -or
        $prefixLength -lt 1 -or $prefixLength -gt 32) {
        throw "容器网段必须是有效的 IPv4 CIDR：$Cidr"
    }

    $blockSize = [uint64][Math]::Pow(2, 32 - $prefixLength)
    $addressNumber = ConvertTo-IPv4Number $address
    $networkNumber = [uint64]([Math]::Floor($addressNumber / $blockSize) * $blockSize)
    $broadcastNumber = $networkNumber + $blockSize - 1

    return [pscustomobject]@{
        Cidr = "$(ConvertFrom-IPv4Number $networkNumber)/$prefixLength"
        PrefixLength = $prefixLength
        Start = $networkNumber
        End = $broadcastNumber
        InputAddress = $addressNumber
    }
}

function Test-IPv4RangeOverlap {
    param(
        [Parameter(Mandatory = $true)]$Left,
        [Parameter(Mandatory = $true)]$Right
    )

    return $Left.Start -le $Right.End -and $Right.Start -le $Left.End
}

function Add-ObservedIPv4Cidr {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][string]$Cidr
    )

    try {
        [void]$Target.Add((Get-IPv4CidrRange $Cidr).Cidr)
    }
    catch {
        # Route tables may contain default, multicast, link-local, or provider-specific
        # entries that are irrelevant to a private Docker bridge. Ignore only those
        # individual entries instead of discarding all usable route information.
    }
}

function Get-ObservedIPv4Ranges {
    $cidrs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    try {
        foreach ($networkInterface in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
            if ($networkInterface.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) {
                continue
            }

            foreach ($addressInformation in $networkInterface.GetIPProperties().UnicastAddresses) {
                if ($addressInformation.Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
                    [System.Net.IPAddress]::IsLoopback($addressInformation.Address) -or
                    $addressInformation.PrefixLength -lt 1 -or
                    $addressInformation.PrefixLength -gt 32) {
                    continue
                }

                $range = Get-IPv4CidrRange "$($addressInformation.Address)/$($addressInformation.PrefixLength)"
                [void]$cidrs.Add($range.Cidr)
            }
        }
    }
    catch {
        Write-Warning "无法完整读取宿主机 IPv4 接口：$($_.Exception.Message)"
    }

    $getNetRouteCommand = Get-Command Get-NetRoute -ErrorAction SilentlyContinue
    if ($null -ne $getNetRouteCommand) {
        try {
            foreach ($route in @(Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop)) {
                if (-not [string]::IsNullOrWhiteSpace($route.DestinationPrefix) -and
                    $route.DestinationPrefix -ne "0.0.0.0/0") {
                    Add-ObservedIPv4Cidr $cidrs $route.DestinationPrefix
                }
            }
        }
        catch {
            Write-Warning "无法完整读取 Windows IPv4 路由表：$($_.Exception.Message)"
        }
    }

    $ipCommand = Get-Command ip -ErrorAction SilentlyContinue
    if ($null -ne $ipCommand) {
        try {
            $routeLines = @(& $ipCommand.Source -o -4 route show 2>$null)
            if ($LASTEXITCODE -eq 0) {
                foreach ($routeLine in $routeLines) {
                    $destination = ([string]$routeLine).Trim().Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries) |
                        Select-Object -First 1
                    if ($destination -match '^\d{1,3}(?:\.\d{1,3}){3}/\d{1,2}$' -and
                        $destination -ne "0.0.0.0/0") {
                        Add-ObservedIPv4Cidr $cidrs $destination
                    }
                }
            }
        }
        catch {
            Write-Warning "无法完整读取 Linux IPv4 路由表：$($_.Exception.Message)"
        }
    }

    $dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -ne $dockerCommand) {
        try {
            $networkIds = @(& $dockerCommand.Source network ls --quiet 2>$null) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            if ($LASTEXITCODE -eq 0 -and $networkIds.Count -gt 0) {
                $inspection = & $dockerCommand.Source network inspect $networkIds 2>$null | Out-String | ConvertFrom-Json
                if ($LASTEXITCODE -eq 0) {
                    foreach ($network in @($inspection)) {
                        foreach ($config in @($network.IPAM.Config)) {
                            if (-not [string]::IsNullOrWhiteSpace($config.Subnet) -and
                                $config.Subnet -match '^\d{1,3}(?:\.\d{1,3}){3}/\d{1,2}$') {
                                [void]$cidrs.Add((Get-IPv4CidrRange $config.Subnet).Cidr)
                            }
                        }
                    }
                }
            }
        }
        catch {
            Write-Warning "无法读取现有 Docker 网络，将继续依据宿主机接口选择网段：$($_.Exception.Message)"
        }
    }

    return @($cidrs | ForEach-Object { Get-IPv4CidrRange $_ })
}

function Select-AvailableContainerSubnet {
    param([Parameter(Mandatory = $true)][array]$ObservedRanges)

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($secondOctet in 20..31) {
        foreach ($blockIndex in 0..15) {
            $candidates.Add("172.$secondOctet.238.$($blockIndex * 16)/28")
        }
    }
    foreach ($thirdOctet in 0..255) {
        $candidates.Add("10.238.$thirdOctet.0/28")
    }
    foreach ($thirdOctet in 240..254) {
        foreach ($blockIndex in 0..15) {
            $candidates.Add("192.168.$thirdOctet.$($blockIndex * 16)/28")
        }
    }

    foreach ($candidate in $candidates) {
        $candidateRange = Get-IPv4CidrRange $candidate
        $conflict = $ObservedRanges | Where-Object {
            Test-IPv4RangeOverlap $candidateRange $_
        } | Select-Object -First 1
        if ($null -eq $conflict) {
            return $candidateRange.Cidr
        }
    }

    throw "未找到可自动分配的容器 /28 网段。请使用 -ContainerSubnet 和 -ReverseProxyIp 显式指定，并先核对 Docker/VPN/局域网路由。"
}

function Get-EnvironmentFileValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $prefix = "$Name="
    $line = Get-Content -LiteralPath $Path |
        Where-Object { $_.StartsWith($prefix, [System.StringComparison]::Ordinal) } |
        Select-Object -Last 1
    if ($null -eq $line) {
        return $null
    }

    return $line.Substring($prefix.Length).Trim()
}

function Assert-SafeDirectoryPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        [System.IO.Path]::TrimEndingDirectorySeparator($fullPath),
        [System.IO.Path]::TrimEndingDirectorySeparator($volumeRoot),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label 不能直接使用磁盘、卷、共享或文件系统根：$fullPath"
    }

    $candidate = $fullPath
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $item = Get-Item -LiteralPath $candidate -Force
            if (-not $item.PSIsContainer) {
                throw "$Label 不能经过普通文件：$candidate"
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label 不能经过符号链接、联接点或其它重解析点：$candidate"
            }
        }

        if ([string]::Equals(
            [System.IO.Path]::TrimEndingDirectorySeparator($candidate),
            [System.IO.Path]::TrimEndingDirectorySeparator($volumeRoot),
            [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = [System.IO.Directory]::GetParent($candidate)
        if ($null -eq $parent) { break }
        $candidate = $parent.FullName
    }

    return $fullPath
}

function Assert-SafeManagedFilePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label 必须是普通文件且不能是符号链接或重解析点：$Path"
    }
}

function Protect-RuntimeConfigurationFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ($IsWindows) {
        try {
            $icacls = Get-Command icacls.exe -ErrorAction Stop
            $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            & $icacls.Source $Path /inheritance:r `
                /grant:r "$($currentIdentity):(M)" `
                /grant:r "*S-1-5-18:(F)" `
                /grant:r "*S-1-5-32-544:(F)" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "无法完全收紧运行配置 ACL，请手工限制该文件访问：$Path"
            }
        }
        catch {
            Write-Warning "未能收紧运行配置 ACL，请手工限制该文件访问：$Path"
        }
        return
    }

    try {
        $ownerOnly = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
        [System.IO.File]::SetUnixFileMode($Path, $ownerOnly)
    }
    catch {
        Write-Warning "未能把运行配置权限设为 600，请手工限制该文件访问：$Path"
    }
}

function Set-UnixRuntimeMode {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.IO.UnixFileMode]$Mode
    )

    if ($IsWindows) {
        return
    }

    [System.IO.File]::SetUnixFileMode($Path, $Mode)
}

function Set-UnixRuntimeOwner {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$UserId,
        [Parameter(Mandatory = $true)][int]$GroupId,
        [switch]$Recursive
    )

    if ($IsWindows) {
        return
    }

    $chown = Get-Command chown -ErrorAction SilentlyContinue
    if ($null -eq $chown) {
        throw "缺少 chown，无法为非 root 容器准备运行目录所有权。"
    }

    $arguments = @()
    if ($Recursive) {
        $arguments += "-R"
    }
    $arguments += "$UserId`:$GroupId"
    $arguments += $Path
    & $chown.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "无法把运行目录所有权设置为 $UserId`:$GroupId：$Path"
    }
}

if ($PostgreSqlPassword.Length -lt 12 -or $PostgreSqlPassword -notmatch '^[A-Za-z0-9._~!@%+=:-]+$') {
    throw "PostgreSQL 密码至少 12 位，且只能使用字母、数字和 . _ ~ ! @ % + = : -，避免 .env 转义歧义。"
}
if ($BootstrapToken.Length -lt 24 -or $BootstrapToken.Length -gt 512 -or $BootstrapToken -notmatch '^[A-Za-z0-9._~!@%+=:-]+$') {
    throw "首次部署令牌必须为 24-512 位，且只能使用字母、数字和 . _ ~ ! @ % + = : -，避免 .env 转义歧义。"
}
if ($WebPort -lt 1 -or $WebPort -gt 65535 -or
    $HttpsPort -lt 1 -or $HttpsPort -gt 65535 -or
    $WebPort -eq $HttpsPort) {
    throw "HTTP/HTTPS 端口必须在 1-65535 之间且不能相同。"
}

$resolvedEnvironmentFile = [System.IO.Path]::GetFullPath($EnvironmentFile)
$environmentRoot = Assert-SafeDirectoryPath (Split-Path -Parent $resolvedEnvironmentFile) "容器环境目录"
Assert-SafeManagedFilePath $resolvedEnvironmentFile "容器环境文件"
$containerSubnetWasProvided = -not [string]::IsNullOrWhiteSpace($ContainerSubnet)
$reverseProxyIpWasProvided = -not [string]::IsNullOrWhiteSpace($ReverseProxyIp)
$networkWasReused = $false
if (-not $containerSubnetWasProvided -and -not $RegenerateNetwork) {
    $existingSubnet = Get-EnvironmentFileValue $resolvedEnvironmentFile "EXPORTDOCMANAGER_CONTAINER_SUBNET"
    if (-not [string]::IsNullOrWhiteSpace($existingSubnet)) {
        $ContainerSubnet = $existingSubnet
        $networkWasReused = $true
        if (-not $reverseProxyIpWasProvided) {
            $ReverseProxyIp = Get-EnvironmentFileValue $resolvedEnvironmentFile "EXPORTDOCMANAGER_REVERSE_PROXY_IP"
        }
    }
}
if ([string]::IsNullOrWhiteSpace($ContainerSubnet) -and $reverseProxyIpWasProvided) {
    throw "单独指定 -ReverseProxyIp 时无法安全推断容器网段；请同时指定 -ContainerSubnet。"
}

$observedRanges = @(Get-ObservedIPv4Ranges)
if ([string]::IsNullOrWhiteSpace($ContainerSubnet)) {
    $ContainerSubnet = Select-AvailableContainerSubnet $observedRanges
}

$containerRange = Get-IPv4CidrRange $ContainerSubnet
if ($containerRange.PrefixLength -lt 24 -or
    $containerRange.PrefixLength -gt 28 -or
    $containerRange.InputAddress -ne $containerRange.Start) {
    throw "容器网段必须是对齐的 IPv4 /24 至 /28；推荐紧凑 /28，例如 172.30.238.0/28：$ContainerSubnet"
}
$privateRanges = @(
    Get-IPv4CidrRange "10.0.0.0/8"
    Get-IPv4CidrRange "172.16.0.0/12"
    Get-IPv4CidrRange "192.168.0.0/16"
)
$isPrivateRange = $privateRanges | Where-Object {
    $containerRange.Start -ge $_.Start -and $containerRange.End -le $_.End
} | Select-Object -First 1
if ($null -eq $isPrivateRange) {
    throw "容器网段必须完整位于 RFC 1918 私有地址范围：$ContainerSubnet"
}
$ContainerSubnet = $containerRange.Cidr

if ([string]::IsNullOrWhiteSpace($ReverseProxyIp)) {
    $ReverseProxyIp = (ConvertFrom-IPv4Number ($containerRange.Start + 10)).ToString()
}
$proxyAddress = $null
if (-not [System.Net.IPAddress]::TryParse($ReverseProxyIp, [ref]$proxyAddress) -or
    $proxyAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Nginx 可信代理地址必须是有效 IPv4：$ReverseProxyIp"
}
$proxyNumber = ConvertTo-IPv4Number $proxyAddress
if ($proxyNumber -le $containerRange.Start -or $proxyNumber -ge $containerRange.End) {
    throw "Nginx 可信代理地址必须位于容器网段内且不能是网络/广播地址：$ReverseProxyIp / $ContainerSubnet"
}
$ReverseProxyIp = $proxyAddress.ToString()

if ($containerSubnetWasProvided -and -not $AllowNetworkOverlap) {
    $conflict = $observedRanges | Where-Object {
        Test-IPv4RangeOverlap $containerRange $_
    } | Select-Object -First 1
    if ($null -ne $conflict) {
        throw "显式容器网段 $ContainerSubnet 与现有网络 $($conflict.Cidr) 重叠。请选择其它网段，或确认后使用 -AllowNetworkOverlap。"
    }
}

$resolvedRuntimeRoot = Assert-SafeDirectoryPath $RuntimeRoot "容器运行数据根"
$apiDataRoot = Assert-SafeDirectoryPath (Join-Path $resolvedRuntimeRoot "api-data") "API 数据目录"
$configRoot = Assert-SafeDirectoryPath (Join-Path $apiDataRoot "Config") "API 配置目录"
New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
$postgresRoot = Assert-SafeDirectoryPath (Join-Path $resolvedRuntimeRoot "postgres") "PostgreSQL 数据目录"
New-Item -ItemType Directory -Force -Path $postgresRoot | Out-Null
$letsencryptRoot = Assert-SafeDirectoryPath (Join-Path $resolvedRuntimeRoot "letsencrypt") "证书目录"
$acmeWebRoot = Assert-SafeDirectoryPath (Join-Path $resolvedRuntimeRoot "acme-webroot") "ACME WebRoot"
New-Item -ItemType Directory -Force -Path $letsencryptRoot | Out-Null
New-Item -ItemType Directory -Force -Path $acmeWebRoot | Out-Null
New-Item -ItemType Directory -Force -Path $environmentRoot | Out-Null
foreach ($directoryPath in @($resolvedRuntimeRoot, $apiDataRoot, $configRoot, $postgresRoot, $letsencryptRoot, $acmeWebRoot, $environmentRoot)) {
    [void](Assert-SafeDirectoryPath $directoryPath "受管容器目录")
}

if (-not $IsWindows) {
    $id = Get-Command id -ErrorAction SilentlyContinue
    if ($null -eq $id -or [int](& $id.Source -u) -ne 0) {
        throw "Linux/macOS 容器初始化必须以 root 运行（例如 sudo pwsh ./initialize-container-runtime.ps1），以便为固定容器 UID 安全设置目录所有权。"
    }

    $ownerOnlyDirectory = [System.IO.UnixFileMode]::UserRead -bor
        [System.IO.UnixFileMode]::UserWrite -bor
        [System.IO.UnixFileMode]::UserExecute
    $containerDirectory = $ownerOnlyDirectory -bor
        [System.IO.UnixFileMode]::GroupRead -bor
        [System.IO.UnixFileMode]::GroupExecute
    $publicReadDirectory = $ownerOnlyDirectory -bor
        [System.IO.UnixFileMode]::GroupRead -bor
        [System.IO.UnixFileMode]::GroupExecute -bor
        [System.IO.UnixFileMode]::OtherRead -bor
        [System.IO.UnixFileMode]::OtherExecute

    Set-UnixRuntimeOwner $resolvedRuntimeRoot 0 0
    Set-UnixRuntimeOwner $apiDataRoot 10001 10001 -Recursive
    Set-UnixRuntimeOwner $postgresRoot 999 999 -Recursive
    Set-UnixRuntimeOwner $letsencryptRoot 0 0 -Recursive
    Set-UnixRuntimeOwner $acmeWebRoot 0 0 -Recursive
    Set-UnixRuntimeMode $resolvedRuntimeRoot $ownerOnlyDirectory
    Set-UnixRuntimeMode $apiDataRoot $containerDirectory
    Set-UnixRuntimeMode $configRoot $containerDirectory
    Set-UnixRuntimeMode $postgresRoot $ownerOnlyDirectory
    Set-UnixRuntimeMode $letsencryptRoot $ownerOnlyDirectory
    Set-UnixRuntimeMode $acmeWebRoot $publicReadDirectory
}

$settings = [ordered]@{
    System = [ordered]@{
        DatabaseProvider = "PostgreSQL"
        SqliteDatabaseFileName = "data.db"
        PostgreSqlHost = "postgres"
        PostgreSqlPort = 5432
        PostgreSqlDatabase = $PostgreSqlDatabase
        PostgreSqlUsername = $PostgreSqlUsername
        PostgreSqlPassword = ""
        PostgreSqlAdditionalOptions = "Pooling=true;Maximum Pool Size=100;Timeout=15;Command Timeout=60"
    }
}
$settingsPath = Join-Path $configRoot "appsettings.json"
Assert-SafeManagedFilePath $settingsPath "应用配置文件"
$settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

$relativeRuntimeRoot = [System.IO.Path]::GetRelativePath($PSScriptRoot, $resolvedRuntimeRoot).Replace("\", "/")
$envLines = @(
    "POSTGRES_DB=$PostgreSqlDatabase",
    "POSTGRES_USER=$PostgreSqlUsername",
    "POSTGRES_PASSWORD=$PostgreSqlPassword",
    "EXPORTDOCMANAGER_BOOTSTRAP_TOKEN=$BootstrapToken",
    "EXPORTDOCMANAGER_WEB_PORT=$WebPort",
    "EXPORTDOCMANAGER_WEB_BIND_ADDRESS=$WebBindAddress",
    "EXPORTDOCMANAGER_HTTPS_PORT=$HttpsPort",
    "EXPORTDOCMANAGER_TLS_CERTIFICATE=./secrets/tls/server.crt",
    "EXPORTDOCMANAGER_TLS_PRIVATE_KEY=./secrets/tls/server.key",
    "EXPORTDOCMANAGER_RUNTIME_ROOT=$relativeRuntimeRoot",
    "EXPORTDOCMANAGER_ALLOWED_ORIGINS=",
    "EXPORTDOCMANAGER_ALLOW_INSECURE_DISASTER_RECOVERY=$($AllowHttpDisasterRecovery.IsPresent.ToString().ToLowerInvariant())",
    "EXPORTDOCMANAGER_CONTAINER_SUBNET=$ContainerSubnet",
    "EXPORTDOCMANAGER_REVERSE_PROXY_IP=$ReverseProxyIp",
    "EXPORTDOCMANAGER_ADDITIONAL_TRUSTED_PROXIES=",
    "TZ=Asia/Shanghai"
)
Assert-SafeManagedFilePath $resolvedEnvironmentFile "容器环境文件"
$envLines | Set-Content -LiteralPath $resolvedEnvironmentFile -Encoding UTF8
if ($IsWindows) {
    Protect-RuntimeConfigurationFile $settingsPath
}
else {
    Set-UnixRuntimeOwner $settingsPath 10001 10001
    $containerOwnerOnly = [System.IO.UnixFileMode]::UserRead -bor
        [System.IO.UnixFileMode]::UserWrite
    Set-UnixRuntimeMode $settingsPath $containerOwnerOnly
}
Protect-RuntimeConfigurationFile $resolvedEnvironmentFile

Write-Host "容器运行目录已初始化: $resolvedRuntimeRoot"
Write-Host "配置文件: $settingsPath"
Write-Host "环境文件: $resolvedEnvironmentFile"
Write-Host "容器网段: $ContainerSubnet"
Write-Host "Nginx 可信代理地址: $ReverseProxyIp"
if ($AllowHttpDisasterRecovery) {
    Write-Warning "已允许纯 HTTP 网页备份恢复和完整迁移；只应在受防火墙保护的可信办公网/VPN 使用。"
}
$networkSource = if ($containerSubnetWasProvided) {
    "管理员显式指定"
} elseif ($networkWasReused) {
    "复用现有 .env（如需重新探测请增加 -RegenerateNetwork）"
} else {
    "根据宿主接口、路由表和 Docker 网络自动选择"
}
Write-Host "网段来源: $networkSource"
Write-Host "下一步: docker compose -f docker-compose.yml --env-file `"$resolvedEnvironmentFile`" up -d --build"
