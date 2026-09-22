using System.Security.Cryptography;
using Hba.Delivery.Application.Ports;

namespace Hba.Delivery.Infrastructure.Persistence;

/// <summary>
/// Référence lisible, dictée au téléphone : alphabet sans 0/O ni 1/I/L, qui sont
/// la première source d'erreur quand un client lit son code à un livreur.
/// </summary>
internal sealed class ReferenceGenerator : IReferenceGenerator
{
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int Length = 6;

    public string NextDeliveryReference()
    {
        Span<char> buffer = stackalloc char[Length];

        for (var i = 0; i < Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return string.Concat("HBA-", new string(buffer));
    }
}
