using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AetherLauncher.Services;

/// <summary>
/// Verschlüsselte Ablage für die .ROBLOSECURITY-Schlüssel.
///
/// <para>
/// Zwei Lagen: Der Inhalt wird mit <b>AES-256-GCM</b> verschlüsselt (Vertraulichkeit und
/// Integrität - ein verändertes Byte macht den Schlüssel unbrauchbar). Der AES-Schlüssel
/// selbst liegt in <c>vault.key</c> und ist mit <b>Windows DPAPI</b> an den angemeldeten
/// Windows-Benutzer gebunden. Kopiert man <c>accounts.json</c> auf einen anderen Rechner
/// oder in ein anderes Windows-Konto, ist er wertlos.
/// </para>
///
/// <para>
/// Alte Werte aus der reinen DPAPI-Version bleiben lesbar und werden beim nächsten Laden
/// automatisch auf das neue Format gehoben.
/// </para>
///
/// <para>
/// Das ist starker Schutz für die <b>gespeicherten</b> Schlüssel, aber keine Magie: Wer den
/// Prozess unter demselben Windows-Benutzer debuggen kann, kann auch die entschlüsselten
/// Werte sehen. Gegen Reverse Engineering des Codes hilft Verschlüsselung allein nicht -
/// dafür sind Obfuskation, Signierung und ein geschützter Build da (siehe README).
/// </para>
/// </summary>
public static class Dpapi
{
    private const uint CryptprotectUiForbidden = 0x1;
    private const string Prefix = "aeg2:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// Zusätzliche DPAPI-Entropie für den Vault-Schlüssel. Ohne diesen Wert kann ein
    /// fremdes Programm <c>vault.key</c> auch unter demselben Windows-Benutzer nicht
    /// mit einem einfachen DPAPI-Aufruf entschlüsseln - es müsste die Konstante aus dem
    /// Build kennen. Kein Ersatz für die DPAPI-Bindung, aber eine weitere Hürde.
    /// </summary>
    private static readonly byte[] VaultEntropy = Encoding.UTF8.GetBytes("AetherLauncher/vault-key/v1");

    private static readonly object KeyLock = new();
    private static byte[]? _key;

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";

        var plain = Encoding.UTF8.GetBytes(plainText);

        try
        {
            return ProtectV2(plain);
        }
        catch (Exception ex)
        {
            // Lieber ein alter, aber funktionierender Schutz als ein Konto, das sich nicht
            // speichern lässt. Der nächste Start versucht die Migration erneut.
            Log.Warn($"AES-GCM is unavailable, falling back to DPAPI for this value: {ex.Message}");
            return Convert.ToBase64String(Transform(plain, protect: true));
        }
        finally
        {
            // Klartext nicht im Speicher liegen lassen; der verschlüsselte Wert ist fertig.
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        try
        {
            if (value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                var plainAes = UnprotectV2(value);
                try
                {
                    return Encoding.UTF8.GetString(plainAes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainAes);
                }
            }

            var plainDpapi = Transform(Convert.FromBase64String(value), protect: false);
            try
            {
                return Encoding.UTF8.GetString(plainDpapi);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainDpapi);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"A stored key could not be decrypted: {ex.Message}");
            return "";
        }
    }

    /// <summary>Liegt der Wert noch im alten DPAPI-Format vor?</summary>
    public static bool NeedsUpgrade(string value) =>
        !string.IsNullOrEmpty(value) && !value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Nur für den Selbsttest: erzeugt einen Wert im alten Format.</summary>
    internal static string ProtectLegacy(string plainText) =>
        Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(plainText), protect: true));

    private static string ProtectV2(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(Key(), TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    private static byte[] UnprotectV2(string value)
    {
        var payload = Convert.FromBase64String(value[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("The stored key is truncated.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(Key(), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    /// <summary>
    /// Der AES-Schlüssel: zufällig erzeugt, einmal pro Windows-Benutzer, DPAPI-geschützt in
    /// <c>vault.key</c>. Fehlt oder bricht die Datei, wird ein neuer Schlüssel erzeugt -
    /// alte Cookies müssen dann neu angemeldet werden.
    /// </summary>
    private static byte[] Key()
    {
        if (_key is not null) return _key;

        lock (KeyLock)
        {
            if (_key is not null) return _key;

            var path = AppPaths.VaultKeyFile;

            if (File.Exists(path))
            {
                try
                {
                    var stored = File.ReadAllText(path).Trim();
                    var blob = Convert.FromBase64String(stored);

                    byte[] key;
                    var legacy = false;

                    try
                    {
                        key = Transform(blob, protect: false, VaultEntropy);
                    }
                    catch (Exception)
                    {
                        // Datei aus der Version ohne eigene Entropie (oder ein anderer
                        // Windows-Benutzer): alte Bindung versuchen und danach aufwerten.
                        key = Transform(blob, protect: false);
                        legacy = true;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(blob);
                    }

                    if (key.Length == KeySize)
                    {
                        if (legacy)
                        {
                            WriteKeyFile(path, key);
                            Log.Info("vault.key was upgraded to app-bound DPAPI entropy.");
                        }

                        return _key = key;
                    }

                    Log.Error($"vault.key has {key.Length} bytes instead of {KeySize}; creating a new key.");
                }
                catch (Exception ex)
                {
                    Log.Error($"vault.key could not be read: {ex.Message}. Creating a new key.");
                }
            }

            var fresh = RandomNumberGenerator.GetBytes(KeySize);
            WriteKeyFile(path, fresh);
            return _key = fresh;
        }
    }

    /// <summary>Schreibt den Vault-Schlüssel atomar und mit eigener DPAPI-Entropie.</summary>
    private static void WriteKeyFile(string path, byte[] key)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var protectedValue = Convert.ToBase64String(Transform(key, protect: true, VaultEntropy));

            // Vor dem Ersetzen prüfen, dass der neue Blob wirklich denselben Schlüssel
            // liefert - ein halb geschriebener Vault würde sonst alle Konten sperren.
            var check = Transform(Convert.FromBase64String(protectedValue), protect: false, VaultEntropy);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(check, key))
                    throw new InvalidOperationException("the re-encrypted vault key does not match");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(check);
            }

            // Keine Sicherung der alten Datei liegen lassen: eine alte Kopie ohne die
            // eigene Entropie wäre ein bequemer Umweg am Schutz vorbei. Erst schreiben,
            // wenn der neue Blob geprüft ist - dann atomar ersetzen.
            var temp = path + ".tmp";
            File.WriteAllText(temp, protectedValue);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            // Der Schlüssel bleibt für diese Sitzung gültig; ohne Datei können alte
            // Werte beim nächsten Start allerdings nicht mehr gelesen werden.
            Log.Error($"vault.key could not be written: {ex.Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    /// <summary>
    /// DPAPI-Aufruf. <paramref name="entropy"/> bindet den Wert zusätzlich an diese
    /// Anwendung; ohne Angabe bleibt die alte, entropielose Variante lesbar.
    /// </summary>
    private static byte[] Transform(byte[] input, bool protect, byte[]? entropy = null)
    {
        var inputBlob = new DataBlob();
        var outputBlob = new DataBlob();
        var entropyBlob = new DataBlob();
        var inputPtr = Marshal.AllocHGlobal(input.Length);
        var entropyPtr = IntPtr.Zero;

        try
        {
            Marshal.Copy(input, 0, inputPtr, input.Length);
            inputBlob.Size = input.Length;
            inputBlob.Data = inputPtr;

            if (entropy is { Length: > 0 })
            {
                entropyPtr = Marshal.AllocHGlobal(entropy.Length);
                Marshal.Copy(entropy, 0, entropyPtr, entropy.Length);
                entropyBlob.Size = entropy.Length;
                entropyBlob.Data = entropyPtr;
            }

            var ok = protect
                ? CryptProtectData(ref inputBlob, "AetherLauncher", ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out outputBlob);

            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
                return result;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPtr);
            if (entropyPtr != IntPtr.Zero) Marshal.FreeHGlobal(entropyPtr);
        }
    }
}
