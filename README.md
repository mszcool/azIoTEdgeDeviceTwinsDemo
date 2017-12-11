# Simple Device Twins Demo for Azure IoT Edge Modules

This is a simple demo showing, how an Azure IoT Edge Module can read (and update) data from device twins created for both, the IoT Edge Gateway Device as well as the IoT Edge Module hosted inside of the Gateway Device.

The requirement came up during a customer project in which the configuration data was required to be available to IoT Edge Modules which are representing various devices at a remote target side backed with an IoT Edge Gateway. Instead of introducing an additional configuration store (on-premises or in the cloud), using device twins for the IoT Edge Gateway as well as their modules to provide configuration data there, simplifies device specific configuration, dramatically, since scale, resiliency and the likes is all managed with and through IoT hub. With that, customers don't need to care about these requirements for additional services (such as a database), which can get quite complex for IoT solutions at scale.

In such configuration scenarios, customers might want to have both,

* configuration data centrally managed for all devices on a site and
* configuration data specific to individual devices.

The first, configuration data shared across multiple devices, is a good candidate for being stored in the IoT Edge Gateway Device's module twin. In turn, configuration data specific to the indvidual devices on a site is a perfect fit for the device twin of the individual module representing a device (or a set of devices).

This requires the capability of reading (and updating) both, the device twins of the modules as well as the gateway device from with one module, directly.

The trick: using Container Create Options
-----------------------------------------

Accessing the device twin is easy since it works with the connection string passed into the environment of the IoT Edge Module's container, directly. Therefore, nothing special except the regular option for the GW-device to access IoT Hub is needed.

But for accessing the twin of the Edge Device from within the module, directly, you need the specific connection string for the Edge Device, itself. This one is currently (as per my knowledge) not passed into the Module's container.

One little trick would be to make use of the container create options when setting a new module and passing the IoT Edge GW Device Connection string through that way as an environment variable:

![]()