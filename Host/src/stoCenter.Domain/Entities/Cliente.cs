using Domain.Enums;

namespace Domain.Entities;

public class Cliente()
{
    public int Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string? Documento { get; private set; } = String.Empty;
    public DateTime DataCadastro { get; private set; }
    public TipoCliente TipoCliente { get; private set; }
}
