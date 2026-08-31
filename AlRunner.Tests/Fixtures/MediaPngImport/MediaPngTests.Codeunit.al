codeunit 72491 "Media PNG Fixture Tests"
{
    Subtype = Test;
    TestPermissions = Disabled;

    [Test]
    procedure ImportPng_PreservesTheNstSaveStreamBytes()
    var
        Row: Record "Media PNG Fixture Row";
        TempBlob: Codeunit "Temp Blob";
        Base64Convert: Codeunit "Base64 Convert";
        SourceOutStream: OutStream;
        SourceInStream: InStream;
        StoredOutStream: OutStream;
        StoredInStream: InStream;
        StoredBase64: Text;
        SourceLength: Integer;
    begin
        TempBlob.CreateOutStream(SourceOutStream);
        Base64Convert.FromBase64(SourceImageBase64(), SourceOutStream);
        SourceLength := TempBlob.Length();
        TempBlob.CreateInStream(SourceInStream);

        Row.Init();
        Row."Entry No." := 1;
        Row.Picture.ImportStream(SourceInStream, 'member.png');
        Row.Insert();
        if not Row.Picture.HasValue() then
            Error('Media.ImportStream did not store the PNG.');

        Clear(TempBlob);
        TempBlob.CreateOutStream(StoredOutStream);
        Row.Picture.ExportStream(StoredOutStream);
        if TempBlob.Length() <> SourceLength then
            Error(
                'Expected the NST saveStream path to preserve %1 bytes, got %2.',
                SourceLength,
                TempBlob.Length());

        TempBlob.CreateInStream(StoredInStream);
        StoredBase64 := Base64Convert.ToBase64(StoredInStream);
        if StoredBase64 <> SourceImageBase64() then
            Error('Media.ImportStream changed the validated PNG source bytes.');
    end;

    local procedure SourceImageBase64(): Text
    begin
        exit(
            'iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAABmJLR0QA/wD/AP+gvaeTAAAACXBIWXMAAAsTAAALEwEAmpwYAAAA' +
            'B3RJTUUH3wwcCikwn4sr7QAAA85JREFUWMO9lk1sG0UUx/9vZndtr6lDlZJGpAGaIj5ECSAOoYIWu1kBrdQLObXlABIFCQhCHECU' +
            'jwupUKHQCxJSEJEQBypEOaA2UlDi1EFVURTTQyNy4EDEAQniJnHJhze7O49Dd82mdbJJHHska7x/j+f39uk/7w1ZVlri+mB/FqHn' +
            'ZZrneTw8/LNnxoztruN2AbibmQHgd8Mwfpi37b/379+nCSF4LfsB8CgUAPmf8MKyZts2JsYnEsXZ4ndE9AwA+HAQEZgZRNSfTKUO' +
            '7959/0IsHsNq+wWa8L9EwvOj+R3F2eLVSvBQEAfnisXC5bHLOxzHiYQDIBH6ccXFjY2N7Cw540SkV4LfoOm2bY8buhEJDzIgVoP3' +
            'ft2r+n/sP01EyTXAgzmZy+ZO9Xzao6KyK24w4E2L21ruYQDda4UHGjO//thDT3BUdkVUmkzDaF8v3NcoGYs9GOEDijShp1TLBuAA' +
            'ANd1W6s3IfPCRuD+w1zVJtzSkMojNNaTjYatW/NVm7AwPTNHRL9uwAf5f6am5qs2YSazV5dSvrZeH2ia1p3J7NWrNqGUkhcd5xII' +
            'H6/DBx8tOs4lKeXmVELLSktH8dsA3ouCCyHeWVLquGWltU2phEdfOKrGRvO3GkKcbtvVdkrTtTsA9BHRtF9wwMwzRPRVPB5vbWpu' +
            '+twQ4rOx0bEt6c50ZJ+hlSJVSiGbHXENId4F0OO/pUtE7ydvSX45c+3fq+HTkTLN20ul0ssAPihnSNBbS576pLPzSY2IKr2gqtiO' +
            'Q/BhAOkV0r7AzH/6/9lJREaldcz8k8P8tH9PQGQ7ZmbOZkdcXYhfVoGDmU0A9xHRvSvB/fGUTpTLZkfcNZlwaCjnGUJ8Q0DHBntA' +
            'JW2fIUTv4OAFZ1UTSikRk/JZAM9tIjyQjhlSHPD1ypVQKUXMfKYG8OunRfHZwcELXsVKeKjrEOeGcicB6LWA+1oirmkn2h9p926q' +
            'hH1f9OkA3qwhHH6Wjx8+0rXchLF4DBPjE921hgfj8Uczr0gp/zfh+XMDLjO/Wmt4oAF4aWBgyC2bMGWaTQDuqgfcv763tzRvT5ZN' +
            'WCqVDtQLHsyFqYIVNuGd9YIHmmL1QNmEesw4W084AGhSu1g24dzC4m8AjtQLToLeWHSc3LJ2vO22bTjz7fdeXNcs5alOZt4D4GEi' +
            'aqgWzsxXiOgiEZ2zPe/8i8eel5N/TFLQjsnPRLADd+zpUCc+PFk+Mwld3+l5XiuAZiJqZOYGBifA0IiIiUgxs83M8wBmSVBBCPFX' +
            'wjQnZ4rXpv0gyLLS0g+qfCf8D8L6EhAUv5Y3AAAAAElFTkSuQmCC');
    end;
}
