program Comp14;
var 
  i, j: integer;
  
begin
  i := 1;
  while i < 11 do begin
    j := 1;
	  while j <= i do begin 
	    Write ('*');
		 j := j + 1;
		 if(j = 6) then break 2;
	  end;
	WriteLn ("");
	i := i + 1;
	if(i = 7) then break;
  end;
end.
