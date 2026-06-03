using System;

class Program
{
    static void Main()
    {
        string[] hashes = {
            "$2a$11$kr2nwn5997f5u8JxORm.QePqd91TxqzaVDBWPMU9zMV59xwhng0iq", // admin
            "$2a$11$97d9Vcgd7jd8RBJ52l8Al.YkRiHzxmDvR05.TkF9DaTz16pU3uS0i", // monitor 1
            "$2a$11$1ShpIx7rv/I9IGDP8mxE/eCNRsanYsnMzXv/xyT8JZ2a0fGM86WhO", // monitor 2
            "$2a$11$fDX.YSw.UtCLsF.6cwD/MO2Pg4JiiWsCBIhay28ToisFyJkVrPoW6", // device 1001
            "$2a$11$QYD3/P1xDKONuNMft9mfvu0EaPGIVOqTf8pL3lWU.sBArvKTE9Nu6", // device 1002
            "$2a$11$5Lw27ACIxHAal2mtESZEl.DUnDy.IqT6QjcwEGuIL9XWgJNxWo7.K"  // device 1003
        };

        string[] guesses = {
            "admin123", "password", "monitor123", "123456", "test1234",
            "device1001", "device1002", "device1003",
            "api-key-1001", "api-key-1002", "api-key-1003",
            "key1001", "key1002", "key1003",
            "1001", "1002", "1003",
            "apikey1001", "apikey1002", "apikey1003"
        };

        foreach (var hash in hashes)
        {
            Console.WriteLine($"Testing hash {hash}...");
            foreach (var guess in guesses)
            {
                if (BCrypt.Net.BCrypt.Verify(guess, hash))
                {
                    Console.WriteLine($"MATCH FOUND: Hash {hash} -> {guess}");
                    break;
                }
            }
        }
    }
}
