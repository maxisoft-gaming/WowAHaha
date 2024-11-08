using System;
using WowAHaha.Utils;

class Program
{
    static void Main()
    {
        Console.WriteLine("Welcome to the Password Encryption Console App!");
        Console.WriteLine("Please enter a password to encrypt:");
        var password = Console.ReadLine();

        Console.WriteLine("Please enter an encryption key:");
        var key = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("Both password and key must be provided. Please try again.");
            return;
        }

        var encryptedPassword = DumbDumbEncryption.Encrypt(password, key);
        Console.WriteLine($"Your encrypted password is: {encryptedPassword}");
    }
}