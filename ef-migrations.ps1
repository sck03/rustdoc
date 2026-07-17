param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("add", "update", "script")]
    [string]$Command = "update",
    [string]$Name = ""
)

$project = "src/ExportDocManager.Infrastructure/ExportDocManager.Infrastructure.csproj"
$startupProject = "src/ExportDocManager.Api/ExportDocManager.Api.csproj"

if ($Command -eq "add") {
    if (-not $Name) {
        Write-Error "请使用 -Name 参数指定迁移名称"
        exit 1
    }
    dotnet ef migrations add $Name --project $project --startup-project $startupProject
}
elseif ($Command -eq "update") {
    dotnet ef database update --project $project --startup-project $startupProject
}
elseif ($Command -eq "script") {
    dotnet ef migrations script --project $project --startup-project $startupProject
}
