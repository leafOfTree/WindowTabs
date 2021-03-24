# WindowTabs

A utility which brings browser-style tabbed window management to the desktop.

<p>
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-example.png" width="300" />
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-win7.jpg" width="300" />
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-Win10.PNG" width="300" />
</p>

## History
It was developed by Maurice Flanagan in 2009 and was provided as a free and paid version originally.   
The author has open-sourced it since no longer has time to maintain it. See the original repository: [mauricef/WindowTabs](https://github.com/mauricef/WindowTabs).

I forked from [payaneco's repository](https://github.com/payaneco/WindowTabs) which forked from [redgis'](https://github.com/redgis/WindowTabs). Now it compiles and runs successfully on both Win7 and Win10.

## Download

It's recommended to compile the exe file as below. If not, you could find my prebuilt files at [releases](https://github.com/leafOfTree/WindowTabs/releases).

## Compilation

It should work on both Win7 and Win10.

- Clone

    ```
    git clone https://github.com/leafOfTree/WindowTabs
    ```

- Install

    - [Visual Studio 2019 community edition](https://visualstudio.microsoft.com/). 

        `.NET desktop development` needs to be selected in the installer.

    - [WiX Toolset build tools V3.11.2](http://wixtoolset.org/releases)

    - [Wix Toolset Visual Studio 2019 Extension](https://marketplace.visualstudio.com/items?itemName=WixToolset.WixToolsetVisualStudio2019Extension)

- Compile

    Lanuch Visual Studio, open this project, and press `Start`. Then it will compile to `WindowTabs\WtProgram\bin\Debug\WindowTabs.exe`.

    If you choose the `Release` configurations, you will get a release version `WindowTabs\WtProgram\bin\Release\WindowTabs.exe`.

## Changes

- Adjust settings font and display
- Fix extra empty tab for File Explorer
- Update packages for Win10
- Fix desktop `Programs` title missing issue

## Refs

- [mauricef/WindowTabs](https://github.com/mauricef/WindowTabs) the origin repository

- [redgis/WindowTabs](https://github.com/redgis/WindowTabs)

- [payaneco/WindowTabs](https://github.com/payaneco/WindowTabs)
