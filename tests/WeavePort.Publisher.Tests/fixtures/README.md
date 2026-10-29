# Independent publisher signature fixture

Generated on 2026-10-29 with OpenSSL 4.1.0, independently of the WeavePort signer. The manifest was assembled as ordinary JSON and file hashes computed with Python hashlib. The RSA private key was temporary and discarded; publisher.der is public SubjectPublicKeyInfo only. These are test bytes, never production keys or executable plugin code.

The signed input is UTF-8 `WeavePort.InstallationSignature.v1\n` followed by the exact release/installation.json bytes. OpenSSL signing used:

```sh
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out private.pem
openssl pkey -in private.pem -pubout -outform DER -out publisher.der
openssl dgst -sha256 -sign private.pem -sigopt rsa_padding_mode:pss \
  -sigopt rsa_pss_saltlen:32 -sigopt rsa_mgf1_md:sha256 -out signature message
```

The envelope stores canonical base64 of signature and lowercase SHA-256 of publisher.der. manifest.sha256 independently fixes the manifest identity. Tests verify with reflection disabled and without Python or OpenSSL installed. No private key is stored in this repository.
