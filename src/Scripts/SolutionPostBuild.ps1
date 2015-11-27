[CmdletBinding()]
param($RepoDir, $SolutionDir, $ConfigurationName, [switch]$Disable)

if(-not ($RepoDir -and $SolutionDir -and $ConfigurationName))
{
	Write-Error "RepoDir, SolutionDir, and ConfigurationName are all required"
	exit 1
}

$netmfVersions = "43","44"
$projects = "PervasiveDigital.Diagnostics","PervasiveDigital.Hardware.ESP8266","PervasiveDigital.Net.Azure.MobileServices","PervasiveDigital.Net.Azure.Storage","PervasiveDigital.Net","PervasiveDigital.Security.ManagedProviders","PervasiveDigital.Utility"

function PublishNugetPackage([string]$projectName, [string]$netmfVersion) {

	$nuspec = $SolutionDir + 'nuget\' + $projectName + '.nuspec'
	Write-Verbose "nuspec file $nuspec"

	$nugetBuildDir = $SolutionDir + 'nuget\' + $ConfigurationName + '\' + $projectName + '\'
	$libDir = $nugetBuildDir + "lib\"
	$srcDir = $nugetBuildDir + "src\"

	Write-Verbose "Nuget build dir is $nugetBuildDir"
	$nuget = $SolutionDir + ".nuget\nuget.exe"

	if (test-path $nugetBuildDir) { ri -r -fo $nugetBuildDir }
	mkdir $libDir | out-null
	mkdir $srcDir | out-null

	$projectDir = $SolutionDir + 'netmf' + $netmfVersion + '\' + $projectName + '\'
	$targetDir = $projectDir + 'bin\' + $ConfigurationName + '\'

	mkdir $libDir"\netmf"$netMFVersion"\be" | out-null
	Copy-Item -Path $targetDir"be\*" -Destination $libDir"\netmf"$netMFVersion"\be" -Include "*.dll","*.pdb","*.xml","*.pdbx","*.pe"
	mkdir $libDir"\netmf"$netMFVersion"\le" | out-null
	Copy-Item -Path $targetDir"le\*" -Destination $libDir"\netmf"$netMFVersion"\le" -Include "*.dll","*.pdb","*.xml","*.pdbx","*.pe"
	Copy-Item -Path $targetDir"*" -Destination $libDir"\netmf"$netMFVersion -Include "*.dll","*.pdb","*.xml"

	# Copy source files for symbol server
	Copy-Item -Recurse -Path $projectDir -Destination $srcDir -Filter "*.cs"
	$target = $srcDir + $projectName
	if (test-path $target"\obj") { Remove-Item -Recurse $target"\obj" | out-null }
	if (test-path $target"\bin") { Remove-Item -Recurse $target"\bin" | out-null }

	# Create the nuget package
	$output = $repoDir + $ConfigurationName
	if (-not (test-path $output)) { mkdir $output | out-null }

	$args = 'pack', $nuspec, '-Symbols', '-basepath', $nugetBuildDir, '-OutputDirectory', $output
	& $nuget $args
}

foreach ($project in $projects) {
	foreach ($version in $netmfVersions) {
		PublishNugetPackage $project $version
	}
}
