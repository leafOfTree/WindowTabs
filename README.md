# WindowTabs

<a href="https://github.com/leafOfTree/WindowTabs">![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/leafoftree/windowtabs/total)</a>

A utility which brings browser-style tabbed window management to the desktop.

<p>
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-example.png" width="260" />
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-win7.jpg" width="260" />
<img alt="screenshot" src="https://raw.githubusercontent.com/leafOfTree/leafOfTree.github.io/master/WindowTabs-Win10.PNG" width="260" />
</p>

## History
It was developed by Maurice Flanagan in 2009 and was provided as free and paid versions originally.   
The author who no longer has time to maintain it has open-sourced it. See the original repository: [mauricef/WindowTabs](https://github.com/mauricef/WindowTabs).

This repository is a fork of [payaneco's repository](https://github.com/payaneco/WindowTabs) which is from [redgis'](https://github.com/redgis/WindowTabs). Now, it compiles and runs successfully on both Win7 and Win10.

## Download

It's recommended to compile the exe file as below. If not, you could find my prebuilt files at [releases](https://github.com/leafOfTree/WindowTabs/releases).

## Compilation

It should work on both Win7 and Win10.

- Clone

    ```
    git clone https://github.com/leafOfTree/WindowTabs
    ```

- Install

    - [Visual Studio 2022 community edition](https://visualstudio.microsoft.com/)

        `.NET desktop development` needs to be selected in the installer.

    - [WiX Toolset build tools V3.14.1](https://wixtoolset.org/docs/wix3/)

    - [Wix Toolset Visual Studio 2022 Extension](https://marketplace.visualstudio.com/items?itemName=WixToolset.WixToolsetVisualStudio2022Extension)

> You have to close Visual Studio to install the extension. Visual Studio 2019 still works.

- Compile and Release

    Lanuch Visual Studio, open this project by "File > Open > Project/Solution", select "WindowTabs.sln".

    If you choose the `Release` configuration and click `Start`, you will get a release version `WindowTabs\WtProgram\bin\Release\WindowTabs.exe`.

- Debug

    Choose `Debug` configuration and it will compile to `WindowTabs\WtProgram\bin\Debug\WindowTabs.exe`.

    Tips: Move your cursor to left gray column and click on it to add a breakpoint on current line. Then start debug and you can see runtime details.

## Changes

2024

- Support Visual Studio 2022

- Remove task window peek (perview) to fix task switch error
- Use the last file name as tab name
- UI improvement on icon and task switch form border

- Add option to deactivate `ctrl+1`... hotkeys
- Add `New window` item to tab context menu
- Support settings file at the same path of exe file

2023

- Recognize ApplicationFrameWindow based Apps like Photo, Mail.
- Fix null exception on toggling Fade out... option.
- Adjust settings font and display.
- Fix extra empty tab for File Explorer.
- Update packages for Win10.
- Fix desktop `Programs` title missing issue.

## Refs

- [mauricef/WindowTabs](https://github.com/mauricef/WindowTabs) the original repository

- [redgis/WindowTabs](https://github.com/redgis/WindowTabs)

- [payaneco/WindowTabs](https://github.com/payaneco/WindowTabs)

- [leafoftree/WindowTabs](https://github.com/leafOfTree/WindowTabs)
