program Comp5;
var 
  i, j: integer;
  
begin
  i := 1;
  while i < 11 do begin
     j := 1;
	  while j <= i do begin 
	    Write ('*');
		 j := j + 1;
		 if(j = 4) then break;
	  end;
	WriteLn ("");
	i := i + 1;
	if(i = 5) then break;
  end;
end.
