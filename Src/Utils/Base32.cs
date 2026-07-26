namespace FileTransmitter;

internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var builder = new System.Text.StringBuilder();
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (byte b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                int index = (bitBuffer >> bitCount) & 0x1F;
                builder.Append(Alphabet[index]);
            }
        }

        if (bitCount > 0)
        {
            int index = (bitBuffer << (5 - bitCount)) & 0x1F;
            builder.Append(Alphabet[index]);
        }

        return builder.ToString();
    }

    public static byte[] Decode(string text)
    {
        text = text.ToUpperInvariant();

        var output = new List<byte>();
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (char c in text)
        {
            int index = Alphabet.IndexOf(c);

            if (index < 0)
                throw new FormatException($"Invalid Base32 character: {c}");

            bitBuffer = (bitBuffer << 5) | index;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                output.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return output.ToArray();
    }
}