[CmdletBinding()]
param($RepoDir, $SolutionDir, $ProjectDir, $ProjectName, $TargetDir, $TargetFileName, $ConfigurationName, $nuspec, $NetMFVersion, [switch]$Disable)

if(-not ($RepoDir -and $SolutionDir -and $ProjectDir -and $ProjectName -and $TargetDir -and $TargetFileName -and $ConfigurationName))
{
	Write-Error "RepoDir, SolutionDir, ProjectDir, TargetDir, TargetFileName and ConfigurationName are all required"
	exit 1
}

if ($nuspec)
{
	Write-Verbose "nuspec file $nuspec"

	$nugetBuildDir = $SolutionDir + "nuget\" + $ConfigurationName + "\" + $ProjectName + "\"
	$libDir = $nugetBuildDir + "lib\"
	$srcDir = $nugetBuildDir + "src\"

	Write-Verbose "Nuget build dir is $nugetBuildDir"
	$nuget = $SolutionDir + ".nuget\nuget.exe"

	if (test-path $nugetBuildDir) { ri -r -fo $nugetBuildDir }
	mkdir $libDir | out-null
	mkdir $srcDir | out-null

	if ($NetMFVersion)
	{
		mkdir $libDir"\netmf"$NetMFVersion"\be" | out-null
		Copy-Item -Path $TargetDir"be\*" -Destination $libDir"\netmf"$NetMFVersion"\be" -Include "*.dll","*.pdb","*.xml","*.pdbx","*.pe"
		mkdir $libDir"\netmf"$NetMFVersion"\le" | out-null
		Copy-Item -Path $TargetDir"le\*" -Destination $libDir"\netmf"$NetMFVersion"\le" -Include "*.dll","*.pdb","*.xml","*.pdbx","*.pe"
		Copy-Item -Path $TargetDir"*" -Destination $libDir"\netmf"$NetMFVersion -Include "*.dll","*.pdb","*.xml"
	}
	else
	{
		mkdir $libDir"\net40" | out-null
		Copy-Item -Path $TargetDir"*" -Destination $libDir"\net40" -Include "*.dll","*.pdb","*.xml"
	}

	# Copy source files for symbol server
	Copy-Item -Recurse -Path $ProjectDir -Destination $srcDir -Filter "*.cs"
	$target = $srcDir + $ProjectName
	if (test-path $target"\obj") { Remove-Item -Recurse $target"\obj" | out-null }
	if (test-path $target"\bin") { Remove-Item -Recurse $target"\bin" | out-null }

	# Create the nuget package
	$output = $repoDir + $ConfigurationName
	if (-not (test-path $output)) { mkdir $output | out-null }

	$args = 'pack', $nuspec, '-Symbols', '-basepath', $nugetBuildDir, '-OutputDirectory', $output
	& $nuget $args
}
