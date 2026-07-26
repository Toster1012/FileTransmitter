using QRCoder;

namespace FileTransmitter;

internal static class QrViewer
{
    public static string GetCodeView(string link)
    {
        ArgumentNullException.ThrowIfNull(link);

        using var qrCodeData = QRCodeGenerator.GenerateQrCode(link, QRCodeGenerator.ECCLevel.Q);
        using var asciiQRCode = new AsciiQRCode(qrCodeData);
        return asciiQRCode.GetGraphicSmall();
    }
}
