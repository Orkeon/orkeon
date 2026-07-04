# -*- coding: utf-8 -*-
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.pdfgen import canvas
import os
OUT="project/experiments/09-factures-extraction/data/uc1_factures"; os.makedirs(OUT,exist_ok=True)
W,H=A4
def euro(x): return f"{x:,.2f}".replace(",", " ").replace(".", ",")+" EUR"
F=[
 ("facture_01","EDF Entreprises","22 av. de Wagram, 75008 Paris","FE-2024-0157","12/01/2024",1250.00,20.0,250.00,1500.00,"tableau"),
 ("facture_02","Orange Business Services","78 rue Olivier de Serres, 75015 Paris","2024-01-18",None,89.90,20.0,17.98,107.88,"minimal"),
 ("facture_03","La Poste - Courrier Pro","9 rue du Colonel Pierre Avia, 75015 Paris","N 4458120","20 janvier 2024",45.50,20.0,9.10,54.60,"clean"),
 ("facture_04","Restaurant Le Gourmet","14 place Bellecour, 69002 Lyon","Note 2024-022","5 fevrier 2024",120.00,10.0,12.00,132.00,"tableau"),
 ("facture_05","Cabinet Juridique Lefevre & Associes","3 bd Haussmann, 75009 Paris","HON-2024-0042","15/02/2024",2000.00,20.0,400.00,2400.00,"clean"),
 ("facture_06","Office Depot France","Immeuble Le Cardinet, 92300 Levallois","OD-778214","2024-02-22",340.75,20.0,68.15,408.90,"tableau"),
 ("facture_07","Editions Dalloz","31 rue du Four, 75006 Paris","AB-2024-3391","3 mars 2024",95.00,5.5,5.23,100.23,"minimal"),
 ("facture_08","LocaMat SARL","Zone Ind. Les Hauts, 33700 Merignac","LM-2024-0210","18/03/2024",600.00,20.0,120.00,720.00,"clean"),
 ("facture_09","Metro Cash & Carry","5 rue des Grands Pres, 92000 Nanterre","2024-03-25 / 99812","25 mars 2024",880.00,5.5,48.40,928.40,"tableau"),
 ("facture_10","SNCF Voyageurs","2 place aux Etoiles, 93200 Saint-Denis","BILLET-2024-55127","2024-03-29",75.00,10.0,7.50,85.00,"minimal"),
]
def hdr(c,f,a,y):
    c.setFont("Helvetica-Bold",16); c.drawString(20*mm,y,f)
    c.setFont("Helvetica",9); c.drawString(20*mm,y-6*mm,a)
    c.drawString(20*mm,y-10*mm,"SIRET 000 000 000 00000 - TVA FR00 000000000")
def cli(c,y):
    c.setFont("Helvetica",9); c.drawString(20*mm,y,"Client : MA PME SARL"); c.drawString(20*mm,y-4*mm,"12 rue de l'Exemple, 75010 Paris")
def L_tab(c,d):
    f,fo,ad,num,da,ht,tx,tv,tt,_=d; hdr(c,fo,ad,H-25*mm)
    c.setFont("Helvetica-Bold",13); c.drawString(20*mm,H-45*mm,"FACTURE"); c.setFont("Helvetica",10)
    c.drawString(20*mm,H-52*mm,f"Facture n {num}")
    if da: c.drawString(20*mm,H-57*mm,f"Date : {da}")
    cli(c,H-70*mm); yt=H-90*mm
    c.setFillColor(colors.HexColor("#34495e")); c.rect(20*mm,yt,170*mm,8*mm,fill=1,stroke=0)
    c.setFillColor(colors.white); c.setFont("Helvetica-Bold",9)
    c.drawString(22*mm,yt+2.3*mm,"Designation"); c.drawString(120*mm,yt+2.3*mm,"Montant HT"); c.drawString(150*mm,yt+2.3*mm,"TVA"); c.drawString(170*mm,yt+2.3*mm,"Total")
    c.setFillColor(colors.black); c.setFont("Helvetica",9)
    c.drawString(22*mm,yt-6*mm,"Prestation / fourniture (voir detail)"); c.drawRightString(145*mm,yt-6*mm,euro(ht)); c.drawString(150*mm,yt-6*mm,f"{tx:g}%"); c.drawRightString(188*mm,yt-6*mm,euro(tt))
    yb=yt-25*mm; c.setFont("Helvetica",10)
    c.drawString(120*mm,yb,"Total HT :"); c.drawRightString(188*mm,yb,euro(ht))
    c.drawString(120*mm,yb-6*mm,f"TVA ({tx:g}%) :"); c.drawRightString(188*mm,yb-6*mm,euro(tv))
    c.setFont("Helvetica-Bold",11); c.drawString(120*mm,yb-14*mm,"Total TTC :"); c.drawRightString(188*mm,yb-14*mm,euro(tt))
def L_min(c,d):
    f,fo,ad,num,da,ht,tx,tv,tt,_=d
    c.setFont("Helvetica-Bold",13); c.drawString(20*mm,H-30*mm,fo); c.setFont("Helvetica",9)
    c.drawString(20*mm,H-40*mm,f"Facture {num}")
    if da: c.drawString(20*mm,H-46*mm,da)
    c.drawString(20*mm,H-56*mm,"Objet : prestation/fourniture"); c.setFont("Helvetica",11)
    c.drawString(20*mm,H-75*mm,f"Montant HT .......... {euro(ht)}")
    c.drawString(20*mm,H-83*mm,f"TVA {tx:g}% ............ {euro(tv)}")
    c.setFont("Helvetica-Bold",12); c.drawString(20*mm,H-93*mm,f"NET A PAYER (TTC) ... {euro(tt)}")
def L_cln(c,d):
    f,fo,ad,num,da,ht,tx,tv,tt,_=d
    c.setStrokeColor(colors.HexColor("#2c7fb8")); c.setLineWidth(2); c.line(20*mm,H-20*mm,190*mm,H-20*mm)
    c.setFillColor(colors.HexColor("#2c7fb8")); c.setFont("Helvetica-Bold",18); c.drawString(20*mm,H-32*mm,fo)
    c.setFillColor(colors.black); c.setFont("Helvetica",9); c.drawString(20*mm,H-38*mm,ad)
    c.setFont("Helvetica-Bold",12); c.drawString(20*mm,H-55*mm,"FACTURE"); c.setFont("Helvetica",10)
    c.drawString(20*mm,H-62*mm,f"Reference : {num}")
    if da: c.drawString(20*mm,H-68*mm,f"Etablie le {da}")
    cli(c,H-82*mm); c.setStrokeColor(colors.HexColor("#dddddd")); c.setLineWidth(0.5); c.line(20*mm,H-100*mm,190*mm,H-100*mm)
    c.setFont("Helvetica",10); c.drawString(20*mm,H-110*mm,"Sous-total HT"); c.drawRightString(190*mm,H-110*mm,euro(ht))
    c.drawString(20*mm,H-117*mm,f"TVA au taux de {tx:g} %"); c.drawRightString(190*mm,H-117*mm,euro(tv))
    c.setFont("Helvetica-Bold",12); c.drawString(20*mm,H-128*mm,"Total a regler TTC"); c.drawRightString(190*mm,H-128*mm,euro(tt))
LY={"tableau":L_tab,"minimal":L_min,"clean":L_cln}
for d in F:
    p=os.path.join(OUT,d[0]+".pdf"); c=canvas.Canvas(p,pagesize=A4); LY[d[9]](c,d)
    c.setFont("Helvetica-Oblique",7); c.setFillColor(colors.grey); c.drawString(20*mm,15*mm,"Document de test Orkeon - donnees fictives")
    c.showPage(); c.save()
print("PDFs:",len(F))
