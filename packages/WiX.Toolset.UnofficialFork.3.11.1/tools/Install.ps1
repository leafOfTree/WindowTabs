# Created by Alexander Köplinger, 2013

param($installPath, $toolsPath, $package, $project)

# ensure that we are installing into a WiX project
if ($project.Kind -ne '{930c7802-8a8c-48f9-8165-68863bccd9dd}')
{
    throw "'$($project.Name)' is not a WiX project! This package will only work on WiX projects."
}

# remove dummy file from project
$dummy = $project.ProjectItems.Item("WiX.Toolset.DummyFile.txt").Delete()

$msBuildProj = @([Microsoft.Build.Evaluation.ProjectCollection]::GlobalProjectCollection.GetLoadedProjects($project.FullName))[0]

# remove previous changes (for cases where Uninstall.ps1 wasn't executed properly)
Import-Module (Join-Path $toolsPath "Remove.psm1")
Remove-Changes $msBuildProj

# add the property group directly before the WixTargetsPath-Import, according to http://wixtoolset.org/documentation/manual/v3/msbuild/daily_builds.html
$propertyGroup = $msBuildProj.Xml.CreatePropertyGroupElement()

$wixImport = $msBuildProj.Xml.Children | Where-Object { $_.Project -eq '$(WixTargetsPath)' }
$msBuildProj.Xml.InsertBeforeChild($propertyGroup, $wixImport)

$propertyGroup.AddProperty('WixToolPath', ('$(SolutionDir)packages\' + $package.Id + '.' + $package.Version + '\tools\wix\'))
$propertyGroup.AddProperty('WixTargetsPath', '$(WixToolPath)wix.targets')
$propertyGroup.AddProperty('WixTasksPath', '$(WixToolPath)WixTasks.dll')

# save changes
$project.Save($null)
$msBuildProj.ReevaluateIfNecessary()

# user needs to restart Visual Studio
# this is crazy but needed to invalidate .targets file cache in VS
Write-Warning "[WiX.Toolset] You need to restart Visual Studio to load the updated WiX Toolset files and prevent build errors."