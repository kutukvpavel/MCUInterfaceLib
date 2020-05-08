# MCUInterfaceLib
A HAL for MCUs connected to PCs via (virtual) UART

This library contains basic OOP-, WMI- and COM-port-related stuff needed to communicate to MCUs using following protocol:
 - MCU is a slave, executing commands and initiating events (alarms), PC is a master, sending requests (commands)
 - Each packet consists of a command designator (first byte), arguments, separated by argument separator byte sequence, and a trailer (command/packet separator byte sequence)
 - Since the packets are sent through UART, they are designed to be human-readable and human-composable (so usually arguments are separated with something like a colon and commands are separated with CR+LF)
 - Each command requires execution confirmation by the MCU (request-response)
 - Presence detection is an ordinary command, but presence answer sequence is unique

You have to extend SimpleDevice class with your device-specific implementation.

The library has classes for hardware entites like:
 - Binary input
 - Binary output
 - Temperature sensor (in fact any non-binary sensor can be jammed into a floating point number, ot the class can be extended)
 - Any class that implements IComparable can be a hardware entity value (see HardwareBase<T>)
 
The library features automatic WMI publishing of the hardware entities (and events), and thus serving as a HAL.

This is a part of an ongoing private project. The example app a is really, really truncated piece of it. But it should still feature WMI publication, because standard HardwareEntities are derived from WmiHardwareBase<T>. Though WmiPublishing probably needs to be enabled (see HardwareEntities' properties) and WmiProvider has to be installed. NB: run VS (or the app) with admin rights for WMI classes to be installed, otherwise WMI won't work, this happens after every dll version change, i.e. every build (check out AssemblyInfo)!
