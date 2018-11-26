[CmdletBinding()]
param($RepoDir, $SolutionDir, $ConfigurationName, [switch]$Disable)

if(-not ($RepoDir -and $SolutionDir -and $ConfigurationName))
{
	Write-Error "RepoDir, SolutionDir, and ConfigurationName are all required"
	exit 1
}

$netmfVersions = "42","43","44"
$projects = "PervasiveDigital.Diagnostics","PervasiveDigital.Hardware.ESP8266","PervasiveDigital.Net.Azure.MobileServices","PervasiveDigital.Net.Azure.Storage","PervasiveDigital.Net","PervasiveDigital.Security.ManagedProviders","PervasiveDigital.Utility"

function CleanNugetPackage([string]$projectName) {

	Write-Verbose "CLEAN"

	$nugetBuildDir = $SolutionDir + 'nuget\' + $ConfigurationName + '\' + $projectName + '\'
	$libDir = $nugetBuildDir + "lib\"
	$srcDir = $nugetBuildDir + "src\"

	Write-Verbose "Nuget build dir is $nugetBuildDir"
	$nuget = $SolutionDir + ".nuget\nuget.exe"

	if (test-path $nugetBuildDir) { ri -r -fo $nugetBuildDir }
	mkdir $libDir | out-null
	mkdir $srcDir | out-null
}


function CopySource([string]$projectName) {

	Write-Verbose "COPYSOURCE"

	$nugetBuildDir = $SolutionDir + 'nuget\' + $ConfigurationName + '\' + $projectName + '\'
	$srcDir = $nugetBuildDir + "src\"

	# Copy source files for symbol server
	$sharedProjectName = $projectName + '.Shared\'
	$sharedDir = $SolutionDir + 'common\' + $sharedProjectName
	Copy-Item -Recurse -Path $sharedDir -Destination $srcDir -Filter "*.cs"
	
	# rename the copied dir to remove the .Shared
	$sharedTargetPath = $srcDir + $sharedProjectName
	Rename-Item -Path $sharedTargetPath -NewName $projectName

	# no longer needed since there are no generated files in shared source projects
	#$target = $srcDir + $projectName
	#if (test-path $target"\obj") { Remove-Item -Recurse $target"\obj" | out-null }
	#if (test-path $target"\bin") { Remove-Item -Recurse $target"\bin" | out-null }
}

function PrepareNugetPackage([string]$projectName, [string]$netmfVersion) {

	Write-Verbose "PREPARE"

	$nugetBuildDir = $SolutionDir + 'nuget\' + $ConfigurationName + '\' + $projectName + '\'
	$libDir = $nugetBuildDir + "lib\"
	$srcDir = $nugetBuildDir + "src\"

	$projectDir = $SolutionDir + 'netmf' + $netmfVersion + '\' + $projectName + '\'
	$targetDir = $projectDir + 'bin\' + $ConfigurationName + '\'

	mkdir $libDir"\netmf"$netMFVersion"\be" | out-null
	Copy-Item -Path $targetDir"be\*" -Destination $libDir"\netmf"$netMFVersion"\be" -Include "$projectname.dll","$projectname.pdb","$projectname.xml","$projectname.pdbx","$projectname.pe"
	mkdir $libDir"\netmf"$netMFVersion"\le" | out-null
	Copy-Item -Path $targetDir"le\*" -Destination $libDir"\netmf"$netMFVersion"\le" -Include "$projectname.dll","$projectname.pdb","$projectname.xml","$projectname.pdbx","$projectname.pe"
	Copy-Item -Path $targetDir"*" -Destination $libDir"\netmf"$netMFVersion -Include "$projectname.dll","$projectname.pdb","$projectname.xml","$projectname.pdbx","$projectname.pe"
}

function PublishNugetPackage([string]$projectName) {

	Write-Verbose "PUBLISH"

	$nuspec = $SolutionDir + 'nuget\' + $projectName + '.nuspec'
	Write-Verbose "nuspec file $nuspec"

	$nugetBuildDir = $SolutionDir + 'nuget\' + $ConfigurationName + '\' + $projectName + '\'
	$libDir = $nugetBuildDir + "lib\"
	$srcDir = $nugetBuildDir + "src\"
	$nuget = $SolutionDir + ".nuget\nuget.exe"

	# Create the nuget package
	$output = $repoDir + $ConfigurationName
	if (-not (test-path $output)) { mkdir $output | out-null }

	$args = 'pack', $nuspec, '-Symbols', '-basepath', $nugetBuildDir, '-OutputDirectory', $output
	& $nuget $args
}

foreach ($project in $projects) {
	CleanNugetPackage $project
	CopySource $project
	foreach ($version in $netmfVersions) {
		PrepareNugetPackage  $project $version
	}
	PublishNugetPackage $project
}
