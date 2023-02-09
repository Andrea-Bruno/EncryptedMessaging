# EncryptedMessaging

### Data exchange library between any type of device, usable for both desktop, mobile and internet of things applications:
* Support of digital signature on packages, and security level in military standard.
* This library, in terms of functionality and type of use, currently has no analogues.

### Examples of use:
* Encrypted communication to create applications similar to Telegram or Signal but with greater attention to computer security and privacy.
* Data transmission between device and cloud.
* Acquisition of telemetry data produced by equipment.
* Connecting the Internet of Things and wearable devices to the cloud.

## The library works correctly under all kinds of circumstances and data lines, and is ready for production scenarios,

Our mission is to exacerbate the concept of security in messaging and create something conceptually new and innovative from a technical point of view. Top-level encrypted communication (there is no backend , there is no server-side contact list, there is no server but a simple router, the theory is that if the server does not exist then the server cannot be hacked, the communication is anonymous, the IDs are derived from a hash of the public keys, therefore in no case it is possible to trace who originates the messages, the encryption key is changed for each single message, and a system of digital signatures guarantees the origin of the messages and prevents attacks "men in de middle"). We use different concepts introduced with Bitcoin technology and the library itself: there are no accounts, the account is simply a pair of public and private keys, groups are also supported, the group ID is derived from a hash computed through the public keys of the members, since the hash process is irreversible, the level of anonymity is maximum). The publication of the source wants to demonstrate the genuineness of the concepts we have adopted! Thanks for your attention!

* [Go to the Messaging Library API Documentation (EncryptedMessaging)](https://www.fuget.org/packages/EncryptedMessaging)

This open source project on github is a tutorial for using and implementing this library in a real use case:

* [Powerful messaging software using this library](https://github.com/Andrea-Bruno/AnonymousMessenger)

* [A robust cross-platform Cloud using this library as an underlying (Encrypted Clod)](https://github.com/Andrea-Bruno/CloudClient)

This project consists of three parts in the form of a library for security and functionality:

* [Secure storage](https://github.com/Andrea-Bruno/SecureStorage) it is a powerful data safe, the cryptographic keys and data that would allow the software to be attacked are kept with this tool.

* [Encrypted messaging](https://github.com/Andrea-Bruno/EncryptedMessaging) it is a powerful low-level cryptographic protocol, of the Trustless type, which manages communication, groups and contacts (this software will never access your address book, this library is the heart of the application)

* [Communication channel](https://github.com/Andrea-Bruno/EncryptedMessaging/tree/master/CommunicationChannel) is the low-level socket communication protocol underlying encrypted communication.

This project uses the Communication Channel project as its underlying, which creates a shcket communication channel for transmitting and receiving encrypted data upstream from the library.
Communication Channel underlies the encrypted messaging protocol, we have separated the two parts because the idea is to provide an universal communication protocol, which can work on any type of communication medium and hardware. Communication Channel creates a tcp socket communication channel, but this underlying one can be replaced with an analogous communication channel working with GSM data networks (without using the internet), or with rs232, rs485 ports, or any other communication devices either digital and analog. Just change the underlying encrypted communication protocol and we can easily implement encrypted communication on any type of device and in any scenario. If necessary, we can create implementations of new communication channels on different hardware, on commission.

These libraries are also distributed as nuget packages:

* [Secure storage (Nuget)](https://www.nuget.org/packages/SecureStorage/)

* [Encrypted messaging (Nuget)](https://www.nuget.org/packages/EncryptedMessaging/)

* [Communication channel (Nuget)](https://www.nuget.org/packages/CommunicationChannel/)

