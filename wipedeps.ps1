$rmTargets = @(
  ".fake",
  "packages"
  "paket-files",
  "paket.lock",
  "build.fsx.lock",
  ".paket",
  ".nuget"
)
Function RmRf {
  param(
    [parameter (Mandatory=$true, position=0, ParameterSetName='Main')]
    [string]$Path
  )
  $relPath=".\$Path"
  
  try {
    Remove-Item -Recurse -ErrorAction:Stop $relPath
  } catch [System.Management.Automation.ItemNotFoundException] {}
}
foreach ($target in $rmTargets) {
  RmRf -Path $target
}