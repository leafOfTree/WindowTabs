# Created by Alexander Köplinger, 2013

function Remove-Changes
{
    param(
        [parameter(Position = 0, Mandatory = $true)]
        [Microsoft.Build.Evaluation.Project]$msBuildProj
    )

    #TODO: this can probably be improved
    $wixToolPathProperties = $msBuildProj.Xml.AllChildren | Where-Object { $_.Value -like '*WiX.Toolset.*\tools\wix\' }
   
    if ($wixToolPathProperties)
    {
        foreach($wixToolPathProperty in $wixToolPathProperties)
        {
            $propertyGroup = $wixToolPathProperty.Parent
            $msBuildProj.Xml.RemoveChild($propertyGroup)
        }
    }
}
