# Created by Alexander Köplinger, 2013

param($installPath, $toolsPath, $package, $project)

$msBuildProj = @([Microsoft.Build.Evaluation.ProjectCollection]::GlobalProjectCollection.GetLoadedProjects($project.FullName))[0]

# remove changes
Import-Module (Join-Path $toolsPath "Remove.psm1")
Remove-Changes $msBuildProj

# need to add and remove a dummy item, otherwise saving the project doesn't work.
$project.ProjectItems.AddFolder("WiX.Toolset.DummyFolder", $null).Delete()

# save changes
$project.Save($null)
$msBuildProj.ReevaluateIfNecessary()

# user needs to restart Visual Studio
# this is crazy but needed to invalidate .targets file cache in VS
Write-Warning "[WiX.Toolset] You need to restart Visual Studio to load the updated WiX Toolset files and prevent build errors."