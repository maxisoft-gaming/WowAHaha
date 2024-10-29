using WowAHaha.Utils;

namespace WowAHaha.Tests;

public class DumbDumbEncryptionTests
{
    [Theory]
    [InlineData("Hello, World!", "secret_key", "!enc:QgEfCwo8f1gKHg8AOw==")]
    [InlineData("", "any_key", "")]
    [InlineData(null, "any_key", null)]
    public void Encrypt_ShouldEncryptAndDecryptCorrectly(string input, string key, string expectedEncrypted)
    {
        // Arrange
        // Act
        var encrypted = DumbDumbEncryption.Encrypt(input, key);

        // Assert
        Assert.Equal(expectedEncrypted, encrypted);

        var decrypted = DumbDumbEncryption.Decrypt(encrypted, key);
        Assert.Equal(input, decrypted);
    }

    [Theory]
    [InlineData("invalid_format", "any_key")]
    public void Decrypt_ShouldReturnOriginalValueForInvalidInput(string input, string key)
    {
        // Arrange
        // Act
        var decrypted = DumbDumbEncryption.Decrypt(input, key);

        // Assert
        Assert.Equal(input, decrypted);
    }
}