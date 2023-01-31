# EncryptedMessaging
## Encrypted communication library to create applications similar to Telegram or Signal but with greater attention to IT security and privacy
Our mission is to exacerbate the concept of security in messaging and create something conceptually new and innovative from a technical point of view. Top-level encrypted communication (there is no backend , there is no server-side contact list, there is no server but a simple router, the theory is that if the server does not exist then the server cannot be hacked, the communication is anonymous, the IDs are derived from a hash of the public keys, therefore in no case it is possible to trace who originates the messages, the encryption key is changed for each single message, and a system of digital signatures guarantees the origin of the messages and prevents attacks "men in de middle"). We use different concepts introduced with Bitcoin technology and the library itself: there are no accounts, the account is simply a pair of public and private keys, groups are also supported, the group ID is derived from a hash computed through the public keys of the members, since the hash process is irreversible, the level of anonymity is maximum). The publication of the source wants to demonstrate the genuineness of the concepts we have adopted! Thanks for your attention!

[Go to the Messaging Library API Documentation (EncryptedMessaging)](https://www.fuget.org/packages/EncryptedMessaging)

This open source project on github is a tutorial for using and implementing this library in a real use case:
[Powerful messaging software using this library](https://github.com/Andrea-Bruno/AnonymousMessenger)

[Go to the Messaging Library API Documentation (EncryptedMessaging)](https://www.fuget.org/packages/EncryptedMessaging)

This project consists of three parts in the form of a library for security and functionality:

[Secure storage](https://github.com/Andrea-Bruno/SecureStorage) it is a powerful data safe, the cryptographic keys and data that would allow the software to be attacked are kept with this tool.
[Encrypted messaging](https://github.com/Andrea-Bruno/EncryptedMessaging) it is a powerful low-level cryptographic protocol, of the Trustless type, which manages communication, groups and contacts (this software will never access your address book, this library is the heart of the application)
[Communication channel](https://github.com/Andrea-Bruno/EncryptedMessaging/tree/master/CommunicationChannel) is the low-level socket communication protocol underlying encrypted communication.

These libraries are also distributed as nuget packages:
[Secure storage (Nuget)](https://www.nuget.org/packages/SecureStorage/)
[Encrypted messaging (Nuget)](https://www.nuget.org/packages/EncryptedMessaging/)
[Communication channel (Nuget)](https://www.nuget.org/packages/CommunicationChannel/)

