// SingleInstance = true: BC gives this exactly one instance per session, so its field
// state — and anything its fields OWN — is the classic thing that outlives a test unless
// the runner scopes it to the isolation boundary.
//
// It carries both probes deliberately. `Bumps` is the plain instance-state probe. The
// `Injector` global plus Arm() is the sharper one, and it is sharper for a specific
// reason: the runner's reset drops its POINTER to the cached instance without ending the
// instance's life (it stays rooted in BC's session tree). Reading `Bumps` cannot tell
// those apart — a fresh instance reports 0 either way. The binding can: it lives on the
// old instance, so it is still there unless something explicitly unbinds it.
codeunit 60984 "Watch Residency State"
{
    SingleInstance = true;

    var
        Injector: Codeunit "Watch Residency Injector B";
        Bumps: Integer;

    procedure Arm()
    begin
        BindSubscription(Injector);
    end;

    procedure Bump()
    begin
        Bumps += 1;
    end;

    procedure BumpCount(): Integer
    begin
        exit(Bumps);
    end;
}
