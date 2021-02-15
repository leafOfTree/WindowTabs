# WindowTabs

WindowTabs is a utility which brings browser-style tabbed window management to the desktop.

It was originally developped by Maurice Flanagan in 2009 and was back then provided as a free and paid version.   
The author has now open-sourced the utility. See the original repository here : https://github.com/mauricef/WindowTabs

I forked from payaneco repository which forked from redgis. Now it compiles and runs successfully on both Win7 and Win10.

<p>
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-win7.jpg" width="300" />
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-Win10.PNG" width="300" />
</p>

It's recommended to compile the exe file by yourself. If not, you could find the prebuilt file at https://github.com/leafOfTree/WindowTabs/releases.

## Compilation

It should work on both Win7 and Win10.

- Install

    - [Visual Studio 2019 community edition](https://visualstudio.microsoft.com/). 

        `.NET desktop development` needs to be selected in the installer.

    - [WiX Toolset build tools V3.11.2](http://wixtoolset.org/releases)

    - [Wix Toolset Visual Studio 2019 Extension](https://marketplace.visualstudio.com/items?itemName=WixToolset.WixToolsetVisualStudio2019Extension)

- Compile

    Lanuch Visual Studio, open WindowTabs project and press `Start`, then it will compile to `WindowTabs\WtProgram\bin\Debug\WindowTabs.exe`.

    If you choose `Release` configurations, you will get a release version `WindowTabs\WtProgram\bin\Release\WindowTabs.exe`.

## Changes

- Adjust settings font and display
- Fix extra empty tab for File Explorer
- Update packages for Win10
- Fix desktop `Programs` title missing issue.

## Refs

- [mauricef/WindowTabs](https://github.com/mauricef/WindowTabs) the origin repository

- [redgis/WindowTabs](https://github.com/redgis/WindowTabs)

- [payaneco/WindowTabs](https://github.com/payaneco/WindowTabs)
