program Comp6;
var
  i, j, k: integer;
begin
  k:= 0;
  for i := 1 to 10 do begin
    WriteLn ( "Loop 1: " ,i);
	if(i = 7) then break;
	if(i = 5) then begin
		for j := 101 to 110 do begin
			if(j = 103) then begin
				while k < 5 do begin	
					if(k = 2) then break;
					WriteLn("Loop 3:", " *");
					k := k + 1;
				end;
			end;
			if(j = 108) then break;			
			WriteLn("Loop 2: ", j);
		end;
	end;
  end;
  WriteLn ();
end.