# developerlab_psvr2
A guide, some scripts and modified Apple unity packages - for getting PSVR2 controllers working in Unity on Vision Pro

I was at the visionOS 26 developer workshop in London last week, and I got PSVR2 controllers working with Unity on visionOS - fully immersive/metal compositor services. Other modes should actually be easier to get working, such as with polyspatial.

It does require a change of the Apple.spatialcontroller packages - removing the Polyspatial dependency. And ensuring the NSUsage descriptions are all filled out in info.plist

After that, you need to bridge whatever framework or input system you are using, which isn't hard - the psvr2 controllers are like others, and for me it worked 1:1 just like a quest controller. Using Apple's accessory helper script in their package is needed for this, so you can map the input using their classes.

Remember to disable the Tracked Pose Drivers on the left and right controllers, it may not work in your setup either. (it should be using Raw ARKit transform)
