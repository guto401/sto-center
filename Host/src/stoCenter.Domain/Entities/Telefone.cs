namespace Domain.Entities;

public class Telefone
{
    /*
        =============
        | ATRIBUTOS |
        =============
    */
    public int Id { get; private set; }
    public string Numero { get; private set; } = string.Empty;
    public string Nome { get; private set; } = string.Empty;

    /*
        ===================
        | RELACIONAMENTOS |
        ===================
    */
    public Cliente Cliente { get; private set; } = null!;

    /*
        ================
        | CONSTRUTORES |
        ================
    */
    private Telefone() {}

    public Telefone(string numero, string nome, Cliente cliente)
    {
        Numero = numero;
        Nome = nome;
        Cliente = cliente;
    }

    /*
        ===========
        | MÉTODOS |
        ===========
    */
    public void Atualizar(string numero, string nome)
    {
        Numero = numero;
        Nome = nome;
    }
}
