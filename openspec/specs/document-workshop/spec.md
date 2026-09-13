## Purpose
Provide an independent document-reader integration template with bounded source transfer, observable reader selection and application-owned complete results.

## Requirements

### Requirement: Installed reader selection
The example SHALL list configured available readers with versions and select an appropriate reader for supported Markdown or plain-text input. Explicit selection SHALL require an installed compatible reader. Automatic fallback SHALL occur only after an explicit content decline.

#### Scenario: Reader substitution and fallback
- **WHEN** the same supported input is imported through the outline or plain reader, or outline content is declined during automatic selection
- **THEN** the selected reader and normalized structure are visible and any fallback is explained

#### Scenario: Missing reader or unsupported media
- **WHEN** the requested reader is unavailable or the media type is unsupported
- **THEN** the import is refused without committing a document

### Requirement: Bounded scoped document transfer
Workers SHALL read captured input through an import-scoped callback without receiving host paths. Requests SHALL validate bound tenant/profile authority, document identity, byte range and transfer limits. Supported input larger than one invocation frame SHALL be processed across multiple reads.

#### Scenario: Large Unicode source
- **WHEN** a supported UTF-8 document exceeds one invocation frame and a code point crosses a read boundary
- **THEN** the complete normalized result retains the decoded content while each transfer remains bounded

#### Scenario: Invalid or revoked read
- **WHEN** a callback lacks its grant, uses another scope, requests an invalid range or exceeds a limit, or follows lease disposal
- **THEN** the callback returns no source bytes

### Requirement: Complete-result ownership
The application SHALL stage extraction results and commit only validated complete output. Cancellation, worker failure, invalid encoding, malformed pages and resource limits SHALL leave no committed partial document and clean up the owned staging area.

#### Scenario: Interrupted extraction
- **WHEN** extraction fails or is cancelled after partial output
- **THEN** no complete document is visible and staging is removed

#### Scenario: Independent concurrent imports
- **WHEN** two tenant/profile imports overlap while one fails
- **THEN** the successful import contains only its own content and can commit independently

### Requirement: Document structure and declared limits
The outline reader SHALL preserve supported heading levels and explicit anchors, and readers SHALL return ordered bounded fragments with metadata identifying the reader and source. Unsupported encoding and limits SHALL fail explicitly rather than truncate successfully.

#### Scenario: Structured document
- **WHEN** a document within the documented grammar and limits completes
- **THEN** its ordered fragments retain the supported headings, anchors and text, and stored metadata identifies the selected reader and source digest
