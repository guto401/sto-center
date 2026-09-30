using Domain.Enums;

namespace Domain.Entities;

public class Cliente()
{
    /*
        =============
        | ATRIBUTOS |
        =============
    */
    public int Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string? Documento { get; private set; } = String.Empty;
    public DateTime DataCadastro { get; private set; }
    public TipoCliente TipoCliente { get; private set; }

    /*
        ===================
        | RELACIONAMENTOS |
        ===================
    */
    // Telefone
    private readonly List<Telefone> _telefones = [];
    public IReadOnlyCollection<Telefone> Telefones => _telefones.AsReadOnly();

    // Email
    private readonly List<Email> _emails = [];
    public IReadOnlyCollection<Email> Emails => _emails.AsReadOnly();

}
