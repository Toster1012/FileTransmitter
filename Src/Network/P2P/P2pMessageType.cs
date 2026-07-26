namespace FileTransmitter;

internal enum P2pMessageType : byte
{
    PublicKey = 0x01,
    AuthTag = 0x02,
    Metadata = 0x03,
    ResumeRequest = 0x04,
    ResumeAck = 0x05,
    Chunk = 0x06,
    Done = 0x07,
    Error = 0x08
}