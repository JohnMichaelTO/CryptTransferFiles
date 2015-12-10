using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public class CryptClass
{
	public CryptClass()
	{
	}

    private struct PasswordCouple
    {
        public byte[] key { get; set; }
        public byte[] iv { get; set; }
    }

    private static PasswordCouple GenerateAlgotihmInputs(string password)
    {
        PasswordCouple result = new PasswordCouple();

        Rfc2898DeriveBytes rfcDb = new Rfc2898DeriveBytes(password, System.Text.Encoding.UTF8.GetBytes(password));

        result.key = rfcDb.GetBytes(16);
        result.iv = rfcDb.GetBytes(16);

        return result;
    }

    public static string EncryptString(string password, string text)
    {
        // Put the clear text into array of bytes
        byte[] plainText = Encoding.UTF8.GetBytes(text);

        // Return the key and initialization vector
        PasswordCouple pass = GenerateAlgotihmInputs(password);

        RijndaelManaged rijndael = new RijndaelManaged();
        // Defines the mode used, key and initialisation vector
        rijndael.Mode = CipherMode.CBC;
        rijndael.Key = pass.key;
        rijndael.IV = pass.iv;

        // Create the encryptor AES - Rijndael
        ICryptoTransform aesEncryptor = rijndael.CreateEncryptor();

        MemoryStream ms = new MemoryStream();

        // Write encrypted data into memory stream
        CryptoStream cs = new CryptoStream(ms, aesEncryptor, CryptoStreamMode.Write);
        cs.Write(plainText, 0, plainText.Length);
        cs.FlushFinalBlock();

        // Put encrypted data into array of bytes
        byte[] encryptedData = ms.ToArray();

        ms.Close();
        cs.Close();

        // Put encrypted data in an encrypted chain in Base64
        return Convert.ToBase64String(encryptedData);
    }

    public static string DecryptString(string password, string text)
    {
        // Put the encrypted text into array of bytes
        byte[] encryptedText = Convert.FromBase64String(text);

        // Return the key and initialization vector
        PasswordCouple pass = GenerateAlgotihmInputs(password);

        RijndaelManaged rijndael = new RijndaelManaged();
        // Defines the mode used
        rijndael.Mode = CipherMode.CBC;
        rijndael.Key = pass.key;
        rijndael.IV = pass.iv;

        // Write decrypted data into memory stream
        ICryptoTransform decryptor = rijndael.CreateDecryptor();
        MemoryStream ms = new MemoryStream(encryptedText);
        CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);

        // Put decrypted data into array of bytes
        byte[] decryptedText = new byte[encryptedText.Length];

        int decryptedByteCount = cs.Read(decryptedText, 0, decryptedText.Length);

        ms.Close();
        cs.Close();

        return Encoding.UTF8.GetString(decryptedText, 0, decryptedByteCount);
    }

    public static void EncryptFile(string password, string file, string encryptedFile)
    {
        // Return the key and initialization vector
        PasswordCouple pass = GenerateAlgotihmInputs(password);

        // FileStream of the file that will be encrypted
        FileStream fsEncryptedFile = new FileStream(encryptedFile, FileMode.Create);

        RijndaelManaged rijndael = new RijndaelManaged();
        // Defines the mode used
        rijndael.Mode = CipherMode.CBC;
        rijndael.Key = pass.key;
        rijndael.IV = pass.iv;

        // Encryptor
        ICryptoTransform aesEncryptor = rijndael.CreateEncryptor();
        CryptoStream cs = new CryptoStream(fsEncryptedFile, aesEncryptor, CryptoStreamMode.Write);

        // FileStream of the file to encrypt
        FileStream fsFile = new FileStream(file, FileMode.OpenOrCreate);

        int data;
        while ((data = fsFile.ReadByte()) != -1)
        {
            cs.WriteByte((byte)data);
        }

        fsFile.Close();
        cs.Close();
        fsEncryptedFile.Close();
    }

    public static void DecryptFile(string password, string encryptedFile, string decryptedFile)
    {
        // Return the key and initialization vector
        PasswordCouple pass = GenerateAlgotihmInputs(password);

        // FileStream of the new file that will be decrypted
        FileStream fsDecryptedFile = new FileStream(decryptedFile, FileMode.Create);

        RijndaelManaged rijndael = new RijndaelManaged();
        // Defines the mode used
        rijndael.Mode = CipherMode.CBC;
        rijndael.Key = pass.key;
        rijndael.IV = pass.iv;

        // Decryptor
        ICryptoTransform aesDecryptor = rijndael.CreateDecryptor();
        CryptoStream cs = new CryptoStream(fsDecryptedFile, aesDecryptor, CryptoStreamMode.Write);

        // FileStream of the file that is currently encrypted
        FileStream fsEncryptedFile = new FileStream(encryptedFile, FileMode.OpenOrCreate);

        int data;
        while ((data = fsEncryptedFile.ReadByte()) != -1)
        {
            cs.WriteByte((byte)data);
        }
        
        try
        {
            cs.Close();
        }
        finally
        {
            fsDecryptedFile.Close();
            fsEncryptedFile.Close();
        }
    }
}
