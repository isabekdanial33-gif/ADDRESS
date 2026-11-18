import sys,glob,os
from PIL import Image,ImageDraw
pat,out=sys.argv[1],sys.argv[2]; cols=int(sys.argv[3]) if len(sys.argv)>3 else 6; size=int(sys.argv[4]) if len(sys.argv)>4 else 256
fs=sorted(glob.glob(pat)); rows=(len(fs)+cols-1)//cols
sheet=Image.new('RGB',(cols*size,rows*(size+16)),(20,20,20)); d=ImageDraw.Draw(sheet)
for i,f in enumerate(fs):
    im=Image.open(f).convert('RGB').resize((size,size)); x=(i%cols)*size; y=(i//cols)*(size+16)
    sheet.paste(im,(x,y+16)); d.text((x+3,y+2),os.path.basename(f)[:40],fill=(255,255,0))
sheet.save(out); print(out,len(fs))
