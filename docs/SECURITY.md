# Classroom privacy and security boundary

The student window and image exporter receive only an approved Program frame. Teacher notes, hidden objects, selection handles and unpublished answers stay in the teacher workspace. Changes that revoke visibility remove the affected objects from previously published and frozen frames. A new lesson stops the external display before loading a new Program.

The project file is local JSON and includes teacher-only information. Store it in a location with appropriate Windows access controls. Export a clean PNG when a recipient only needs the approved visual content. The app validates file size, version, fields, object counts, coordinates and notation before opening a project. Save and export write a new file and replace the chosen destination after writing succeeds. Diagnostic entries contain an exception type and result code.

The desktop checks public release metadata over HTTPS, validates the version and expected Windows installer name, and opens the repository's versioned download in the system browser. Each release includes SHA-256 checksums. Authenticode signing and automatic installation are future release gates.

Owner roles, school isolation, MFA, beta invitations, cloud data retention and recording/broadcast consent are future implementation gates. See [requirements status](REQUIREMENTS.md).
