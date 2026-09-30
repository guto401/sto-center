namespace Domain.Entities;
public class Email
{
    public int Id { get; private set; }
    public string EmailString { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;

    // Relacionamento
    public Cliente Cliente { get; private set; } = null!;

}
