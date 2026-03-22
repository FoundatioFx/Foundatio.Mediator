namespace Products.Module.Versions.V2025_06_01;

// Only the messages whose response shape changed in this version.
public record GetProduct(string ProductId);

public record GetProducts();
