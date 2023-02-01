# EncryptedMessaging
## Encrypted communication library to create applications similar to Telegram or Signal but with greater attention to IT security and privacy
Our mission is to exacerbate the concept of security in messaging and create something conceptually new and innovative from a technical point of view. Top-level encrypted communication (there is no backend , there is no server-side contact list, there is no server but a simple router, the theory is that if the server does not exist then the server cannot be hacked, the communication is anonymous, the IDs are derived from a hash of the public keys, therefore in no case it is possible to trace who originates the messages, the encryption key is changed for each single message, and a system of digital signatures guarantees the origin of the messages and prevents attacks "men in de middle"). We use different concepts introduced with Bitcoin technology and the library itself: there are no accounts, the account is simply a pair of public and private keys, groups are also supported, the group ID is derived from a hash computed through the public keys of the members, since the hash process is irreversible, the level of anonymity is maximum). The publication of the source wants to demonstrate the genuineness of the concepts we have adopted! Thanks for your attention!

* [Go to the Messaging Library API Documentation (EncryptedMessaging)](https://www.fuget.org/packages/EncryptedMessaging)

This open source project on github is a tutorial for using and implementing this library in a real use case:

* [Powerful messaging software using this library](https://github.com/Andrea-Bruno/AnonymousMessenger)

* [Go to the Messaging Library API Documentation (EncryptedMessaging)](https://www.fuget.org/packages/EncryptedMessaging)

Nuget packages of this library:

* [Encrypted messaging (Nuget)](https://www.nuget.org/packages/EncryptedMessaging/)

This project uses the Communication Channel project as its underlying, which creates a shcket communication channel for transmitting and receiving encrypted data upstream from the library.
Communication Channel underlies the encrypted messaging protocol, we have separated the two parts because the idea is to provide an universal communication protocol, which can work on any type of communication medium and hardware. Communication Channel creates a tcp socket communication channel, but this underlying one can be replaced with an analogous communication channel working with GSM data networks (without using the internet), or with rs232, rs485 ports, or any other communication devices either digital and analog. Just change the underlying encrypted communication protocol and we can easily implement encrypted communication on any type of device and in any scenario. If necessary, we can create implementations of new communication channels on different hardware, on commission.

* [Communication Channel (TCP socket connection)] (https://github.com/Andrea-Bruno/EncryptedMessaging/tree/master/CommunicationChannel)

The reasons that led to this project with dontnet is that it is an open source development environment, and effective security is achieved only by being able to inspect all parts of the code, including the development framework.
* [.NET is open source](https://dotnet.microsoft.com/en-us/platform/open-source)

Our target is very linux oriented, and the partnership between Microsoft and Canonical ensure the highest standard of security and reliability.

* [Microsoft and Canonical: partnering for security](https://ubuntu.com/blog/install-dotnet-on-ubuntu)

* [Red Hat works with Microsoft to ensure new major versions and service releases are available in tandem with Microsoft releases](https://developers.redhat.com/products/dotnet/overview)
