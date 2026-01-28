# Script para generar claves DKIM (RSA 2048 bit) usando RSACryptoServiceProvider
# Compatible con versiones antiguas y modernas de PowerShell/Windows.

$keySize = 2048
$privateKeyFile = "dkim_private.pem"
$publicKeyFile = "dkim_public.pem"

Write-Host "Generando par de claves RSA $keySize bits (Legacy Provider)..."

try {
    # Usar RSACryptoServiceProvider
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider($keySize)

    # 1. Clave Privada (PKCS#1)
    # Nota: Exportar a PEM manualmente porque RSACryptoServiceProvider exporta XML por defecto.
    # Pero para DKIM y MimeKit, necesitamos formato PEM o XML. MimeKit soporta XML?
    # Mejor intentaremos obtener el blob y formatear.
    
    # Si tenemos .NET nuevo, podemos usar métodos nuevos sobre el objeto RSA base
    if ($rsa.GetType().GetMethod("ExportPkcs8PrivateKey")) {
        $privBytes = $rsa.ExportPkcs8PrivateKey()
        $privBase64 = [Convert]::ToBase64String($privBytes, [Base64FormattingOptions]::InsertLineBreaks)
        $privPem = "-----BEGIN PRIVATE KEY-----`r`n" + $privBase64 + "`r`n-----END PRIVATE KEY-----"
        Set-Content -Path $privateKeyFile -Value $privPem -Encoding Ascii
        Write-Host "Clave Privada (PKCS#8) generada OK."
    }
    else {
        # Fallback a XML si es PS muy viejo (raro hoy en dia en dev)
        $xml = $rsa.ToXmlString($true)
        Set-Content -Path "dkim_private.xml" -Value $xml -Encoding Ascii
        Write-Host "Clave Privada guardada como XML (dkim_private.xml) - Actualice su configuración para usar XML o convierta a PEM."
    }

    # 2. Clave Pública
    # ExportSubjectPublicKeyInfo existe en .NET Framework 4.7.2+ y Core
    if ($rsa.GetType().GetMethod("ExportSubjectPublicKeyInfo")) {
        $pubBytes = $rsa.ExportSubjectPublicKeyInfo()
        $pubBase64 = [Convert]::ToBase64String($pubBytes, [Base64FormattingOptions]::InsertLineBreaks)
        $pubPem = "-----BEGIN PUBLIC KEY-----`r`n" + $pubBase64 + "`r`n-----END PUBLIC KEY-----"
        Set-Content -Path $publicKeyFile -Value $pubPem -Encoding Ascii
        
        # Valor limpio para DNS
        $dnsValue = [Convert]::ToBase64String($pubBytes)
        
        Write-Host "Clave Pública generada OK."
        Write-Host ""
        Write-Host "=== DATOS PARA CLOUDFLARE ==="
        Write-Host "Registro: TXT"
        Write-Host "Nombre:   default._domainkey"
        Write-Host "Valor:    v=DKIM1; k=rsa; p=$dnsValue"
        Write-Host "============================="
    }
    else {
        Write-Host "Error: Su versión de .NET es muy antigua para exportar SPKI nativamente."
    }

}
catch {
    Write-Host "Error fatal: $_"
}
