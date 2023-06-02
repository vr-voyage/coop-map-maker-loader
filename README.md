# Cooperative Map Maker - Unity Load script

A script to recreate the spawned elements in the Cooperative Map Maker available here.

This showcases how to have a cooperative map making tool inside VRChat.

## Requirements

* Unity 2019.4.31f
* VRChat World SDK

In order to use this Editor script, you currently need the VRChat SDK.

This is **NOT** due to the script having VRChat dependencies.

This is due to Unity default settings being unable to read 32-bits EXR data out of the box, even when setting the texture format to RGBAFloat.

VRChat applies modification to the Unity project that allows the script to perform correctly.  
Once I understand which modifications are performed, I'll remove the VRChat requirement.

