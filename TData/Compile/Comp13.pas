program Comp5;
var 
  i, j: integer;
  
begin
  i := 0;
  repeat
     Write ('+');
	  i := i + 1;
	  if(i = 5) then break;
  until i = 10;
  WriteLn ("");
end.
